using System.Diagnostics;
using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <summary>
/// هسته‌ی برنامه: کشف و لود پلاگین‌ها، مسیریابی کوئری بین آن‌ها و اجرای ردیف انتخاب‌شده.
/// هر پلاگین در برابر خطا و کندی ایزوله است؛ یک پلاگین خراب فقط خودش را از نتایج حذف می‌کند.
/// </summary>
public sealed class PluginEngine
{
    private readonly FileLogger _log = new("engine");
    private readonly PluginDiscovery _discovery;
    private readonly CsxPluginLoader _loader;
    private readonly SettingsStore _settingsStore;

    private List<PluginDescriptor> _plugins = [];
    private StoreClient? _store;

    public PluginEngine()
    {
        _discovery = new PluginDiscovery(_log);
        _loader = new CsxPluginLoader(_log);
        _settingsStore = new SettingsStore(_log);
        Settings = _settingsStore.Load();
        Usage = new UsageStore(_log);
    }

    public AppSettings Settings { get; private set; }
    public UsageStore Usage { get; }

    /// <summary>کلاینت فروشگاه؛ با تغییر آدرس در تنظیمات دوباره ساخته می‌شود.</summary>
    public StoreClient Store => _store ??= new StoreClient(Settings.StoreUrl, _log);

    /// <summary>همه‌ی پلاگین‌های کشف‌شده (شامل غیرفعال‌ها و خراب‌ها) برای صفحه‌ی تنظیمات.</summary>
    public IReadOnlyList<PluginDescriptor> Plugins => _plugins;

    /// <summary>پلاگین‌های آماده‌ی پاسخ‌گویی، مرتب بر اساس آخرین استفاده — لیست حالت پیش‌فرض.</summary>
    public IReadOnlyList<PluginDescriptor> PluginsByRecency()
        => _plugins
            .Where(p => p.State == PluginState.Loaded)
            .OrderByDescending(p => Usage.LastUsedOf(p.Id))
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>کشف و لود همه‌ی پلاگین‌های فعال. پلاگین‌های خراب با وضعیت Failed باقی می‌مانند.</summary>
    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        PluginPaths.EnsureCreated();

        var stopwatch = Stopwatch.StartNew();
        var discovered = _discovery.Discover();

        await Task.WhenAll(discovered.Select(p => LoadOneAsync(p, cancellationToken))).ConfigureAwait(false);

        _plugins = discovered.ToList();

        var loaded = _plugins.Count(p => p.State == PluginState.Loaded);
        _log.Info($"loaded {loaded} of {_plugins.Count} plugins in {stopwatch.ElapsedMilliseconds}ms");
    }

    private async Task LoadOneAsync(PluginDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled(descriptor.Id))
        {
            descriptor.State = PluginState.Disabled;
            return;
        }

        try
        {
            var instance = await _loader.LoadAsync(descriptor, cancellationToken).ConfigureAwait(false);
            var context = new PluginContext(descriptor.Manifest, descriptor.Directory);

            await instance.InitializeAsync(context, cancellationToken).ConfigureAwait(false);

            descriptor.Instance = instance;
            descriptor.Context = context;
            descriptor.State = PluginState.Loaded;
            descriptor.Error = null;
        }
        catch (Exception ex)
        {
            descriptor.State = PluginState.Failed;
            descriptor.Error = ex is PluginLoadException ple ? ple.Details : ex.Message;
            descriptor.Instance = null;
            _log.Error($"failed to load plugin \"{descriptor.Id}\"", ex);
        }
    }

    /// <summary>
    /// اجرای کوئری روی پلاگین‌های مرتبط و برگرداندن نتایج مرتب‌شده.
    ///
    /// اگر هیچ پلاگینی چیزی برنگرداند، یک‌بار دیگر با خوانشِ اصلاح‌شده‌ی چیدمان کیبورد امتحان می‌شود
    /// (مثلاً «زاقخپث» که با زبان فارسیِ جامانده تایپ شده، در واقع «chrome» است). این تلاش دوم فقط
    /// وقتی انجام می‌شود که تلاش اول خالی باشد، پس هیچ‌وقت جای یک نتیجه‌ی واقعی را نمی‌گیرد.
    /// </summary>
    public async Task<QueryOutcome> QueryAsync(string raw, CancellationToken cancellationToken = default)
    {
        raw ??= string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return QueryOutcome.Empty;

        var items = await QueryTargetsAsync(raw, cancellationToken).ConfigureAwait(false);
        if (items.Count > 0) return new QueryOutcome(items, null);

        if (!KeyboardLayout.TryFix(raw, out var corrected)) return QueryOutcome.Empty;

        var alternative = await QueryTargetsAsync(corrected, cancellationToken).ConfigureAwait(false);
        if (alternative.Count == 0) return QueryOutcome.Empty;

        _log.Info($"keyboard layout fix: \"{raw}\" read as \"{corrected}\" ({alternative.Count} results)");
        return new QueryOutcome(alternative, corrected);
    }

    private async Task<IReadOnlyList<SearchItem>> QueryTargetsAsync(string raw, CancellationToken cancellationToken)
    {
        var targets = ResolveTargets(raw);
        if (targets.Count == 0) return [];

        var tasks = targets.Select(t => QueryOneAsync(t.Plugin, t.Query, cancellationToken));
        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);

        return batches
            .SelectMany(b => b)
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Settings.MaxResults)
            .ToList();
    }

    /// <summary>
    /// اگر کوئری با کلیدواژه‌ی یک پلاگین شروع شود فقط همان پلاگین صدا زده می‌شود؛
    /// در غیر این صورت پلاگین‌های بدون کلیدواژه (سراسری) صدا زده می‌شوند.
    /// </summary>
    private List<(PluginDescriptor Plugin, PluginQuery Query)> ResolveTargets(string raw)
    {
        var ready = _plugins.Where(p => p.State == PluginState.Loaded && p.Instance is not null).ToList();
        var targets = new List<(PluginDescriptor, PluginQuery)>();

        foreach (var plugin in ready)
        {
            var keyword = plugin.MatchKeyword(raw);
            if (keyword is null) continue;

            var search = raw.TrimStart()[keyword.Length..].TrimStart();
            targets.Add((plugin, new PluginQuery(raw, keyword, search)));
        }

        if (targets.Count > 0) return targets;

        foreach (var plugin in ready.Where(p => p.Manifest.Keywords.Count == 0))
            targets.Add((plugin, new PluginQuery(raw, string.Empty, raw.Trim())));

        return targets;
    }

    private async Task<IReadOnlyList<SearchItem>> QueryOneAsync(
        PluginDescriptor plugin,
        PluginQuery query,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Settings.QueryTimeoutMs);

        try
        {
            var results = await plugin.Instance!.QueryAsync(query, timeout.Token).ConfigureAwait(false)
                          ?? [];

            return results
                .Where(r => !string.IsNullOrWhiteSpace(r.Title))
                .Select(r =>
                {
                    var item = new SearchItem { Plugin = plugin, Result = r };
                    item.Score = r.Score + Usage.BoostFor(item.UsageKey);
                    return item;
                })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // کوئری بعدی کاربر این یکی را لغو کرده است
            return [];
        }
        catch (OperationCanceledException)
        {
            _log.Warn($"plugin \"{plugin.Id}\" did not answer within {Settings.QueryTimeoutMs}ms");
            return [];
        }
        catch (Exception ex)
        {
            _log.Error($"query failed for plugin \"{plugin.Id}\"", ex);
            return [];
        }
    }

    /// <summary>اجرای ردیف انتخاب‌شده؛ خروجی مشخص می‌کند پنجره بسته شود یا نه.</summary>
    public async Task<bool> ExecuteAsync(SearchItem item, CancellationToken cancellationToken = default)
    {
        Usage.Record(item.UsageKey);
        Usage.Record(item.Plugin.Id);
        Usage.Flush();

        try
        {
            return await item.Result.InvokeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error($"action failed for result \"{item.Title}\" of plugin \"{item.Plugin.Id}\"", ex);
            return false;
        }
    }

    /// <summary>فعال/غیرفعال کردن پلاگین و اعمال فوری آن.</summary>
    public async Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
    {
        Settings.DisabledPlugins.RemoveAll(id => id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (!enabled) Settings.DisabledPlugins.Add(pluginId);
        _settingsStore.Save(Settings);

        var descriptor = _plugins.FirstOrDefault(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) return;

        if (enabled)
        {
            await LoadOneAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            descriptor.Instance = null;
            descriptor.Context = null;
            descriptor.State = PluginState.Disabled;
        }
    }

    /// <summary>حذف کامل پلاگین از دیسک به‌همراه آمار استفاده‌اش.</summary>
    public void Uninstall(string pluginId)
    {
        var descriptor = _plugins.FirstOrDefault(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null) return;

        descriptor.Instance = null;
        descriptor.Context = null;

        try
        {
            if (Directory.Exists(descriptor.Directory)) Directory.Delete(descriptor.Directory, recursive: true);
            _plugins.Remove(descriptor);
            Usage.Forget(pluginId);
            Settings.DisabledPlugins.RemoveAll(id => id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
            _settingsStore.Save(Settings);
            _log.Info($"plugin \"{pluginId}\" removed");
        }
        catch (Exception ex)
        {
            _log.Error($"failed to remove plugin \"{pluginId}\"", ex);
            throw;
        }
    }

    /// <summary>کشف دوباره‌ی پوشه‌ی پلاگین‌ها (بعد از نصب دستی یا ویرایش کد).</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default) => LoadAllAsync(cancellationToken);

    /// <summary>نسخه‌ی نصب‌شده‌ی یک پلاگین، یا null اگر نصب نباشد.</summary>
    public string? InstalledVersionOf(string pluginId)
        => _plugins.FirstOrDefault(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))?.Manifest.Version;

    /// <summary>نصب یا به‌روزرسانی یک بسته از فروشگاه و لود دوباره‌ی پلاگین‌ها.</summary>
    public async Task InstallFromStoreAsync(StorePlugin plugin, CancellationToken cancellationToken = default)
    {
        await Store.InstallAsync(plugin, cancellationToken).ConfigureAwait(false);

        // پلاگین قبلی ممکن است غیرفعال شده باشد؛ نصب از فروشگاه یعنی کاربر آن را می‌خواهد
        Settings.DisabledPlugins.RemoveAll(id => id.Equals(plugin.Id, StringComparison.OrdinalIgnoreCase));
        _settingsStore.Save(Settings);

        await LoadAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SaveSettings() => _settingsStore.Save(Settings);

    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(settings);
    }
}
