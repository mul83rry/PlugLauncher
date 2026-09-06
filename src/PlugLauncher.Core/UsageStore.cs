using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlugLauncher.Core;

/// <summary>یک ردیف آمار استفاده.</summary>
public sealed class UsageEntry
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("lastUsed")]
    public DateTimeOffset LastUsed { get; set; }
}

/// <summary>
/// آمار استفاده (تعداد + آخرین بار) برای مرتب‌سازی نتایج و لیست پیش‌فرض پلاگین‌ها.
/// روی دیسک در <c>usage.json</c> نگه‌داری می‌شود؛ نوشتن با تأخیر انجام می‌شود تا هر Enter یک I/O نشود.
/// </summary>
public sealed class UsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly FileLogger _log;
    private readonly ConcurrentDictionary<string, UsageEntry> _entries;
    private int _dirty;

    public UsageStore(FileLogger? logger = null)
    {
        _log = logger ?? new FileLogger("usage");
        _entries = new ConcurrentDictionary<string, UsageEntry>(Load(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>ثبت یک استفاده. کلید ردیف‌ها <c>pluginId::resultId</c> و برای خود پلاگین <c>pluginId</c> است.</summary>
    public void Record(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        _entries.AddOrUpdate(
            key,
            _ => new UsageEntry { Count = 1, LastUsed = DateTimeOffset.Now },
            (_, existing) =>
            {
                existing.Count++;
                existing.LastUsed = DateTimeOffset.Now;
                return existing;
            });

        Interlocked.Exchange(ref _dirty, 1);
    }

    public UsageEntry? Get(string key)
        => _entries.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>امتیاز تقویتی برای مرتب‌سازی: هرچه بیشتر و تازه‌تر استفاده شده باشد، بالاتر.</summary>
    public int BoostFor(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) return 0;

        var daysAgo = (DateTimeOffset.Now - entry.LastUsed).TotalDays;
        var recency = daysAgo switch
        {
            < 1 => 60,
            < 7 => 40,
            < 30 => 20,
            _ => 5
        };

        return recency + Math.Min(entry.Count, 40);
    }

    /// <summary>آخرین زمان استفاده از یک پلاگین؛ برای لیست پیش‌فرض.</summary>
    public DateTimeOffset LastUsedOf(string pluginId)
        => _entries.TryGetValue(pluginId, out var entry) ? entry.LastUsed : DateTimeOffset.MinValue;

    /// <summary>حذف آمار یک پلاگین و همه‌ی ردیف‌هایش (موقع حذف پلاگین).</summary>
    public void Forget(string pluginId)
    {
        foreach (var key in _entries.Keys)
        {
            if (key.Equals(pluginId, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(pluginId + "::", StringComparison.OrdinalIgnoreCase))
                _entries.TryRemove(key, out _);
        }

        Interlocked.Exchange(ref _dirty, 1);
        Flush();
    }

    /// <summary>نوشتن روی دیسک، فقط اگر چیزی تغییر کرده باشد.</summary>
    public void Flush()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PluginPaths.UsageFile)!);
            var snapshot = _entries.ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(PluginPaths.UsageFile, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.Error("ذخیره‌ی usage.json شکست خورد", ex);
        }
    }

    private Dictionary<string, UsageEntry> Load()
    {
        try
        {
            if (File.Exists(PluginPaths.UsageFile))
            {
                var json = File.ReadAllText(PluginPaths.UsageFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, UsageEntry>>(json, JsonOptions);
                if (data is not null) return data;
            }
        }
        catch (Exception ex)
        {
            _log.Error("خواندن usage.json شکست خورد", ex);
        }

        return [];
    }
}
