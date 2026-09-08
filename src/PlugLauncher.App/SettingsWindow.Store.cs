using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>بخش فروشگاه از صفحه‌ی تنظیمات: فهرست بسته‌ها، نصب و به‌روزرسانی.</summary>
public partial class SettingsWindow
{
    private CancellationTokenSource? _storeCts;
    private bool _storeLoaded;

    /// <summary>اولین باری که کاربر وارد تب فروشگاه می‌شود فهرست گرفته می‌شود، نه در باز شدن پنجره.</summary>
    private async void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is not TabControl tab) return;
        if (tab.SelectedIndex != 1 || _storeLoaded) return;

        _storeLoaded = true;
        await LoadStoreAsync(null);
    }

    private async void OnStoreSearch(object sender, RoutedEventArgs e) => await LoadStoreAsync(StoreSearchBox.Text);

    private async void OnStoreRefresh(object sender, RoutedEventArgs e) => await LoadStoreAsync(StoreSearchBox.Text);

    private async void OnStoreSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        await LoadStoreAsync(StoreSearchBox.Text);
    }

    private async Task LoadStoreAsync(string? query)
    {
        _storeCts?.Cancel();
        _storeCts = new CancellationTokenSource();
        var token = _storeCts.Token;

        StoreStatus.Text = "Loading…";
        StoreList.ItemsSource = null;

        try
        {
            var plugins = await _engine.Store.SearchAsync(query, token);
            if (token.IsCancellationRequested) return;

            var rows = plugins
                .Select(p => new StoreRow(p, _engine.InstalledVersionOf(p.Id)))
                .ToList();

            StoreList.ItemsSource = rows;
            StoreStatus.Text = rows.Count == 0
                ? "No packages found"
                : $"{rows.Count} package(s)";

            // آیکن‌ها بعد از نمایش لیست، یکی‌یکی و بدون بلاک کردن UI می‌آیند
            foreach (var row in rows)
            {
                if (token.IsCancellationRequested) return;
                await row.LoadIconAsync(_engine.Store, token);
            }
        }
        catch (OperationCanceledException)
        {
            // جستجوی بعدی جای این یکی را گرفته است
        }
        catch (Exception ex)
        {
            StoreStatus.Text = $"Error: {ex.Message}";
        }
    }

    private async void OnInstallFromStore(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pluginId } button) return;
        if (StoreList.ItemsSource is not List<StoreRow> rows) return;

        var row = rows.FirstOrDefault(r => r.Id == pluginId);
        if (row is null) return;

        button.IsEnabled = false;
        StoreStatus.Text = $"Installing {row.Name}…";

        try
        {
            await _engine.InstallFromStoreAsync(row.Plugin);

            StoreStatus.Text = $"{row.Name} v{row.Plugin.Version} installed";
            Refresh();
            await LoadStoreAsync(StoreSearchBox.Text);
        }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            StoreStatus.Text = $"Install failed: {ex.Message}";
            MessageBox.Show(ex.Message, "Install plugin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _storeCts?.Cancel();
        base.OnClosed(e);
    }

    /// <summary>یک ردیف در لیست فروشگاه به‌همراه وضعیت نصب‌بودنش روی این سیستم.</summary>
    private sealed class StoreRow(StorePlugin plugin, string? installedVersion) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public StorePlugin Plugin { get; } = plugin;

        public string Id => Plugin.Id;
        public string Name => string.IsNullOrWhiteSpace(Plugin.Name) ? Plugin.Id : Plugin.Name;
        public string Description => Plugin.Description;

        public string VersionText => installedVersion is null
            ? $"v{Plugin.Version}"
            : $"v{Plugin.Version}  ·  installed v{installedVersion}";

        /// <summary>دلیلِ اینکه این بسته اینجا کار نمی‌کند، یا null. جلوی نصبش را می‌گیرد.</summary>
        private string? Rejected { get; } = PluginSupport.Reject(plugin.Platforms, plugin.MinCore);

        public string MetaText
        {
            get
            {
                // دلیلِ نصب نشدن اول می‌آید: بقیه‌ی خط وقتی به درد می‌خورد که بشود نصبش کرد
                var parts = new List<string>();
                if (Rejected is not null) parts.Add(Rejected);

                parts.Add($"{Plugin.Size / 1024.0:0.#} KB");
                parts.Add($"{Plugin.Downloads} downloads");
                if (!string.IsNullOrWhiteSpace(Plugin.Author)) parts.Add(Plugin.Author);
                if (Plugin.Keywords.Count > 0) parts.Add("keywords: " + string.Join(", ", Plugin.Keywords));
                return string.Join("  ·  ", parts);
            }
        }

        /// <summary>نصب / به‌روزرسانی / نصب‌شده — بر اساس مقایسه‌ی نسخه‌ی نصب‌شده با فروشگاه.</summary>
        public string ActionText => Rejected is not null
            ? "Unavailable"
            : installedVersion switch
            {
                null => "Install",
                _ when !installedVersion.Equals(Plugin.Version, StringComparison.OrdinalIgnoreCase) => "Update",
                _ => "Installed"
            };

        public bool CanInstall => Rejected is null &&
                                  (installedVersion is null ||
                                   !installedVersion.Equals(Plugin.Version, StringComparison.OrdinalIgnoreCase));

        public ImageSource? Icon { get; private set; }

        /// <summary>دانلود آیکن بسته؛ خطا نادیده گرفته می‌شود و ردیف بدون آیکن می‌ماند.</summary>
        public async Task LoadIconAsync(StoreClient client, CancellationToken cancellationToken)
        {
            var bytes = await client.DownloadIconAsync(Plugin, cancellationToken);
            if (bytes is null || bytes.Length == 0) return;

            try
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.DecodePixelHeight = 80;
                image.EndInit();
                image.Freeze();

                Icon = image;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
            catch
            {
                // آیکن خراب؛ ردیف بدون آیکن نمایش داده می‌شود
            }
        }
    }
}
