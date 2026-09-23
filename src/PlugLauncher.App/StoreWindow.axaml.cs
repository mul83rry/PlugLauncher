using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PlugLauncher.Contracts;
using PlugLauncher.Core;
using PlugLauncher.PluginUI;

namespace PlugLauncher.App;

/// <summary>
/// پنجره‌ی فروشگاه پلاگین — به سبک استور ویندوز ۱۱: ریل ناوبری سمت چپ، خانه با بنر و ردیف‌های
/// افقی، مرورِ گریدی، صفحه‌ی جزئیات و کتابخانه. داده همان <c>index.json</c> استاتیکی است که تبِ
/// قدیمی فروشگاه می‌خواند؛ فقط ظاهر و جریان عوض شده.
/// </summary>
public partial class StoreWindow : Window
{
    private enum Page
    {
        Home,
        Browse,
        Library,
        Detail
    }

    private readonly PluginEngine _engine;
    private readonly List<StoreItem> _items = [];
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(220) };

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _shotsCts;
    private string _lastQuery = "";
    private Page _page;
    private Page _detailReturn;
    private StoreItem? _detail;
    private StoreItem? _hero;

    public StoreWindow(PluginEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Icon = App.LoadAppIcon();

        // جستجوی زنده: هر تایپ تایمر را صفر می‌کند و فقط وقتی انگشتی نماند گرفته می‌شود
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            ApplySearch();
        };
        SearchBox.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };

        SetPage(Page.Home);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_items.Count == 0) _ = LoadAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        _shotsCts?.Cancel();
        base.OnClosed(e);
    }

    // ============ بارگذاری داده ============

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        LoadingBar.IsVisible = true;
        StatusText.Text = "Loading…";

        try
        {
            var plugins = await _engine.Store.SearchAsync(null, token);
            if (token.IsCancellationRequested) return;

            _items.Clear();
            _items.AddRange(plugins.Select(p => new StoreItem(p, _engine.InstalledVersionOf(p.Id))));

            StatusText.Text = _items.Count == 0 ? "The store is empty" : $"{_items.Count} plugins";
            RebuildHome();
            RebuildBrowse();
            RebuildLibrary();
            if (_page == Page.Detail && _detail is not null) RefreshDetailState();

            // آیکن‌ها بعد از چیدنِ لیست می‌آیند؛ هر آیکن با PropertyChanged خودش جای خودش را روشن می‌کند
            foreach (var item in _items)
            {
                if (token.IsCancellationRequested) return;
                await item.LoadIconAsync(_engine.Store, token);
            }
        }
        catch (OperationCanceledException)
        {
            // بارگذاری بعدی جای این را گرفته است
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            if (_items.Count == 0) ShowLoadError(ex.Message);
        }
        finally
        {
            if (!token.IsCancellationRequested) LoadingBar.IsVisible = false;
        }
    }

    private void ShowLoadError(string message)
    {
        BrowseList.ItemsSource = null;
        BrowseEmptyText.Text = message;
        BrowseEmpty.IsVisible = true;
        BrowseRetry.IsVisible = true;
        SetPage(Page.Browse);
    }

    // ============ ناوبری ============

    private void OnNavHome(object? sender, RoutedEventArgs e) => SetPage(Page.Home);

    private void OnNavBrowse(object? sender, RoutedEventArgs e) => SetPage(Page.Browse);

    private void OnNavLibrary(object? sender, RoutedEventArgs e) => SetPage(Page.Library);

    private void OnBack(object? sender, RoutedEventArgs e) => SetPage(_detailReturn);

    private void SetPage(Page page)
    {
        _page = page;

        HomeRoot.IsVisible = page == Page.Home;
        BrowseRoot.IsVisible = page == Page.Browse;
        LibraryRoot.IsVisible = page == Page.Library;
        DetailRoot.IsVisible = page == Page.Detail;
        BackButton.IsVisible = page == Page.Detail;

        NavHome.Classes.Set("active", page == Page.Home);
        NavBrowse.Classes.Set("active", page == Page.Browse);
        NavLibrary.Classes.Set("active", page == Page.Library);
    }

    // ============ جستجو ============

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _debounce.Stop();
            ApplySearch();
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(SearchBox.Text))
        {
            e.Handled = true;
            SearchBox.Text = string.Empty;
        }
    }

    private void ApplySearch()
    {
        var query = (SearchBox.Text ?? string.Empty).Trim();
        _lastQuery = query;

        // تایپ از هر صفحه‌ای — حتی وسط جزئیات — کاربر را به نتیجه‌ها می‌برد
        if (_page != Page.Browse && query.Length > 0) SetPage(Page.Browse);

        RebuildBrowse();
    }

    private List<StoreItem> Filtered(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _items;

        var needle = query.Trim();
        return _items.Where(i =>
            i.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || i.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || i.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || i.Plugin.Keywords.Any(k => k.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    // ============ چیدن صفحات ============

    private void RebuildBrowse()
    {
        var view = Filtered(_lastQuery);
        BrowseList.ItemsSource = view;

        var empty = view.Count == 0;
        BrowseEmpty.IsVisible = empty;
        if (empty)
        {
            BrowseEmptyText.Text = string.IsNullOrWhiteSpace(_lastQuery)
                ? "The store has nothing to offer yet"
                : "No plugins match your search";
            BrowseRetry.IsVisible = false;
        }
    }

    private void RebuildHome()
    {
        var byDate = _items.OrderByDescending(i => i.Plugin.PublishedAt).ToList();

        var hero = byDate.FirstOrDefault();
        HeroPanel.IsVisible = hero is not null;
        if (hero is not null)
        {
            if (_hero is not null) _hero.PropertyChanged -= OnHeroChanged;
            _hero = hero;
            hero.PropertyChanged += OnHeroChanged;

            HeroIcon.Source = hero.Icon;
            HeroName.Text = hero.Name;
            HeroDesc.Text = hero.Description;
            HeroPrimary.Tag = hero.Id;
            HeroDetails.Tag = hero.Id;
            HeroPrimary.Content = hero.ActionText;
            HeroPrimary.IsEnabled = hero.CanInstall;
        }

        var recent = byDate.Skip(1).Take(8).ToList();
        HomeRecentSection.IsVisible = recent.Count > 0;
        HomeRecentList.ItemsSource = recent;

        var installed = _items.Where(i => i.IsInstalled).ToList();
        HomeInstalledSection.IsVisible = installed.Count > 0;
        HomeInstalledList.ItemsSource = installed;
    }

    private void OnHeroChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hero is null) return;

        if (e.PropertyName == nameof(StoreItem.Icon)) HeroIcon.Source = _hero.Icon;
        else
        {
            HeroPrimary.Content = _hero.ActionText;
            HeroPrimary.IsEnabled = _hero.CanInstall;
        }
    }

    private void RebuildLibrary()
    {
        var updates = _items.Where(i => i.HasUpdate).ToList();
        LibUpdatesSection.IsVisible = updates.Count > 0;
        LibUpdatesList.ItemsSource = updates;

        LibInstalledList.ItemsSource = _engine.Plugins
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new InstalledRow(p))
            .ToList();
    }

    // ============ صفحه‌ی جزئیات ============

    private void OnOpenDetail(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;

        ShowDetail(item);
    }

    private void OnOpenHeroDetail(object? sender, RoutedEventArgs e)
    {
        if (_hero is null) return;
        ShowDetail(_hero);
    }

    private void ShowDetail(StoreItem item)
    {
        _detailReturn = _page == Page.Detail ? _detailReturn : _page;
        if (_detail is not null) _detail.PropertyChanged -= OnDetailChanged;
        _detail = item;
        item.PropertyChanged += OnDetailChanged;

        // گالری عکس‌ها؛ دانلودِ صفحه‌ی قبلی کنسل می‌شود و این بسته از کش خودش نشان می‌دهد
        _shotsCts?.Cancel();
        _shotsCts = new CancellationTokenSource();
        DetailShots.ItemsSource = item.ScreenshotImages;
        DetailShotsScroller.IsVisible = item.HasScreenshots;
        _ = item.LoadScreenshotsAsync(_engine.Store, _shotsCts.Token);

        DetailIcon.Source = item.Icon;
        DetailName.Text = item.Name;
        DetailByline.Text = string.IsNullOrWhiteSpace(item.Plugin.Author)
            ? $"v{item.Plugin.Version}"
            : $"by {item.Plugin.Author}  ·  v{item.Plugin.Version}";

        DetailDescription.Text = string.IsNullOrWhiteSpace(item.Description)
            ? "No description provided."
            : item.Description;

        DetailVersion.Text = $"v{item.Plugin.Version}";
        DetailPublished.Text = item.DateText.Length > 0 ? item.DateText : "—";
        DetailSize.Text = item.SizeText.Length > 0 ? item.SizeText : "—";
        DetailPlatforms.Text = item.PlatformsText;
        DetailRequires.Text = item.MinCoreText;
        DetailId.Text = item.Id;

        RefreshDetailState();
        SetPage(Page.Detail);
    }

    private void RefreshDetailState()
    {
        if (_detail is null) return;

        DetailAction.Tag = _detail.Id;
        DetailAction.Content = _detail.ActionText;
        DetailAction.IsEnabled = _detail.CanInstall;

        DetailRemove.IsVisible = _detail.IsInstalled;
        DetailRemove.Tag = _detail.Id;

        DetailRejected.Text = _detail.RejectedReason;
        DetailRejected.IsVisible = _detail.IsRejected;
    }

    private void OnDetailChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_detail is null) return;

        if (e.PropertyName == nameof(StoreItem.Icon)) DetailIcon.Source = _detail.Icon;
        else RefreshDetailState();
    }

    // ============ نصب و حذف ============

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item?.CanInstall != true) return;

        // اگر دکمه داخل کارت است، کلیک نباید به کل کارت برسد تا جزئیات باز شود
        e.Handled = true;

        var progress = new Progress<DownloadProgress>(p =>
        {
            DetailProgress.IsVisible = true;
            if (p.TotalBytes is > 0)
            {
                DetailProgress.IsIndeterminate = false;
                DetailProgress.Maximum = p.TotalBytes.Value;
                DetailProgress.Value = Math.Min(p.Received, p.TotalBytes.Value);
            }
            else
            {
                DetailProgress.IsIndeterminate = true;
            }
        });

        item.SetBusy(true);
        StatusText.Text = $"Installing {item.Name}…";

        try
        {
            PluginWindows.CloseFor(item.Id);
            await _engine.InstallFromStoreAsync(item.Plugin, CancellationToken.None, progress);

            item.SetInstalled(_engine.InstalledVersionOf(item.Id));
            StatusText.Text = $"{item.Name} v{item.Plugin.Version} installed";

            RebuildHome();
            RebuildLibrary();
            RefreshDetailState();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Install failed: {ex.Message}";
            Dialog.Info("Install plugin", ex.Message);
        }
        finally
        {
            item.SetBusy(false);
            DetailProgress.IsVisible = false;
        }
    }

    private void OnRemoveInstalled(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (!Dialog.Confirm("Remove plugin", $"Remove plugin \"{id}\" and all of its files?")) return;

        try
        {
            PluginWindows.CloseFor(id);
            _engine.Uninstall(id);
            StatusText.Text = $"{id} removed";

            var item = _items.FirstOrDefault(i => i.Id == id);
            item?.SetInstalled(null);

            RebuildHome();
            RebuildLibrary();
            RefreshDetailState();
        }
        catch (Exception ex)
        {
            Dialog.Info("Error", $"Could not remove: {ex.Message}");
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = LoadAsync();

    // ============ مدل‌های نمایش ============

    /// <summary>یک بسته‌ی فروشگاه به‌همراه وضعیت نصب روی این سیستم و آیکنِ در حال آمدن.</summary>
    private sealed class StoreItem(StorePlugin plugin, string? installedVersion) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public StorePlugin Plugin { get; } = plugin;

        public string Id => Plugin.Id;

        public string Name => string.IsNullOrWhiteSpace(Plugin.Name) ? Plugin.Id : Plugin.Name;

        public string Description => Plugin.Description;

        /// <summary>نسخه‌ی نصب‌شده روی این سیستم؛ null یعنی نصب نیست.</summary>
        public string? InstalledVersion { get; private set; } = installedVersion;

        public bool IsBusy { get; private set; }

        /// <summary>دلیلِ کار نکردن این بسته اینجا، یا null.</summary>
        private string? Rejected { get; } = PluginSupport.Reject(plugin.Platforms, plugin.MinCore);

        public bool IsRejected => Rejected is not null;

        public bool IsInstalled => InstalledVersion is not null;

        public bool HasUpdate => InstalledVersion is not null
                                 && !InstalledVersion.Equals(Plugin.Version, StringComparison.OrdinalIgnoreCase);

        public string ActionText => Rejected is not null ? "Unavailable"
            : IsBusy ? "Installing…"
            : InstalledVersion is null ? "Install"
            : HasUpdate ? "Update"
            : "Installed";

        public bool CanInstall => Rejected is null && !IsBusy && (InstalledVersion is null || HasUpdate);

        public string AuthorLine => string.IsNullOrWhiteSpace(Plugin.Author)
            ? $"v{Plugin.Version}"
            : Plugin.Author;

        public string SizeText => Plugin.Size <= 0 ? string.Empty : $"{Plugin.Size / 1024.0:0.#} KB";

        public string DateText => Plugin.PublishedAt == default
            ? string.Empty
            : Plugin.PublishedAt.UtcDateTime.ToLocalTime().ToString("d");

        public string CardMeta => string.Join("  ·  ", new[] { SizeText, DateText }.Where(s => s.Length > 0));

        public string PlatformsText => Plugin.Platforms.Count == 0 ? "Any" : string.Join(", ", Plugin.Platforms);

        public string MinCoreText => string.IsNullOrWhiteSpace(Plugin.MinCore) ? "Any" : $"PlugLauncher {Plugin.MinCore}+";

        public string UpdateLine => InstalledVersion is null
            ? string.Empty
            : $"v{InstalledVersion}  →  v{Plugin.Version}";

        public string RejectedReason => Rejected ?? string.Empty;

        public Bitmap? Icon { get; private set; }

        /// <summary>گالری اسکرین‌شات‌ها؛ هر عکس که برسد همان‌جا ظاهر می‌شود.</summary>
        public ObservableCollection<Bitmap> ScreenshotImages { get; } = [];

        private bool _screenshotsLoaded;

        public bool HasScreenshots => Plugin.Screenshots.Count > 0;

        /// <summary>
        /// دانلود اسکرین‌شات‌ها، یکی‌یکی و فقط یک‌بار برای هر بسته؛ بعد از آن از همین کش دوباره
        /// نشان داده می‌شوند. عکسِ خراب یا نیامده بی‌صدا جا می‌ماند.
        /// </summary>
        public async Task LoadScreenshotsAsync(StoreClient client, CancellationToken cancellationToken)
        {
            if (_screenshotsLoaded || Plugin.Screenshots.Count == 0) return;
            _screenshotsLoaded = true;

            foreach (var url in Plugin.Screenshots)
            {
                var bytes = await client.DownloadImageAsync(url, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                if (bytes is null || bytes.Length == 0) continue;

                try
                {
                    using var stream = new MemoryStream(bytes);
                    // دو برابر ارتفاع نمایش، تا روی صفحه‌های با مقیاس بالا هم تیز بماند
                    ScreenshotImages.Add(Bitmap.DecodeToHeight(stream, 420));
                }
                catch
                {
                    // عکس خراب؛ بقیه ادامه می‌یابند
                }
            }
        }

        /// <summary>دانلود آیکن بسته؛ خطا نادیده گرفته می‌شود و کارت بدون آیکن می‌ماند.</summary>
        public async Task LoadIconAsync(StoreClient client, CancellationToken cancellationToken)
        {
            var bytes = await client.DownloadIconAsync(Plugin, cancellationToken);
            if (bytes is null || bytes.Length == 0) return;

            try
            {
                using var stream = new MemoryStream(bytes);
                // بزرگ‌تر از نیازِ کارت دیکود می‌شود تا در صفحه‌ی جزئیات هم تیز بماند
                Icon = Bitmap.DecodeToHeight(stream, 96);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
            catch
            {
                // آیکن خراب؛ کارت بدون آیکن نمایش داده می‌شود
            }
        }

        public void SetBusy(bool busy)
        {
            if (IsBusy == busy) return;
            IsBusy = busy;
            Notify(nameof(ActionText), nameof(CanInstall));
        }

        public void SetInstalled(string? version)
        {
            if (string.Equals(InstalledVersion, version, StringComparison.OrdinalIgnoreCase)) return;
            InstalledVersion = version;
            Notify(nameof(ActionText), nameof(CanInstall), nameof(UpdateLine));
        }

        private void Notify(params string[] names)
        {
            foreach (var name in names)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>یک پلاگین نصب‌شده (از فروشگاه یا همراه خود برنامه) در فهرست کتابخانه.</summary>
    private sealed class InstalledRow(PluginDescriptor plugin)
    {
        public string Id => plugin.Id;

        public string Name => string.IsNullOrWhiteSpace(plugin.Name) ? plugin.Id : plugin.Name;

        public string VersionText => plugin.State == PluginState.Disabled
            ? $"v{plugin.Manifest.Version}  ·  disabled"
            : $"v{plugin.Manifest.Version}";

        public string Initial => string.IsNullOrWhiteSpace(plugin.Name)
            ? "?"
            : plugin.Name.Trim()[0].ToString().ToUpperInvariant();
    }
}
