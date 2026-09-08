using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using PlugLauncher.Core;
using PlugLauncher.Platform;

namespace PlugLauncher.App;

public partial class SettingsWindow : Window
{
    private readonly PluginEngine _engine;

    /// <summary>موقع پر کردن کنترل‌ها از روی وضعیت فعلی، هندلرها نباید چیزی بنویسند.</summary>
    private bool _loading;

    /// <summary>به‌روزرسانی‌ای که الان نشان داده می‌شود، برای دکمه‌ی دانلود.</summary>
    private UpdateInfo? _update;

    /// <param name="knownUpdate">
    /// اگر استارتاپ نسخه‌ی جدیدی پیدا کرده باشد، همان‌جا نشان داده می‌شود تا برای دیدنش لازم
    /// نباشد دوباره از گیت‌هاب پرسیده شود.
    /// </param>
    public SettingsWindow(PluginEngine engine, UpdateInfo? knownUpdate = null)
    {
        _engine = engine;
        InitializeComponent();

        Icon = App.LoadAppIcon();

        _loading = true;
        HotkeyBox.Text = _engine.Settings.Hotkey;
        // اسمِ سیستم داخل متن است، چون این چک‌باکس روی هر سه سیستم چیز متفاوتی می‌نویسد —
        // کلید رجیستری، فایل plist، یا فایل desktop
        StartWithSystemBox.Content = $"Start with {Os.DisplayName}";
        // از خود سیستم خوانده می‌شود نه از settings.json، چون کاربر می‌تواند از بیرون هم عوضش کند
        StartWithSystemBox.IsChecked = Os.AutoStart.IsEnabled();
        AutoUpdateBox.IsChecked = _engine.Settings.CheckForUpdates;
        _loading = false;

        VersionText.Text = $"PlugLauncher v{UpdateChecker.RunningVersion.ToString(3)}";
        if (knownUpdate is not null) ShowUpdate(knownUpdate);

        Refresh();
    }

    private void OnAutoUpdateToggled(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _engine.Settings.CheckForUpdates = AutoUpdateBox.IsChecked == true;
        _engine.SaveSettings();
    }

    /// <summary>
    /// بررسی دستی. عمداً محدودیت «روزی یک‌بار» را دور می‌زند — کاربر خودش دکمه را زده و انتظار
    /// جواب تازه دارد، نه جواب دیروز.
    /// </summary>
    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        UpdateCheckButton.IsEnabled = false;
        UpdateDownloadButton.IsVisible = false;
        UpdateStatus.Text = "Checking…";
        _update = null;

        try
        {
            using var checker = new UpdateChecker(UpdateChecker.RunningVersion);
            var result = await checker.CheckAsync();

            if (!result.Succeeded)
            {
                UpdateStatus.Text = $"Could not check — {result.Error}";
                return;
            }

            _engine.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _engine.SaveSettings();

            if (result.Update is null)
            {
                UpdateStatus.Text = "Up to date";
                return;
            }

            ShowUpdate(result.Update);
        }
        finally
        {
            UpdateCheckButton.IsEnabled = true;
        }
    }

    private void ShowUpdate(UpdateInfo update)
    {
        _update = update;
        UpdateStatus.Text = $"Version {update.Version.ToString(3)} is available";
        UpdateDownloadButton.IsVisible = true;
    }

    /// <summary>
    /// صفحه‌ی ریلیز باز می‌شود، نه یک فایل مشخص: نصب با installer و اجرای zip دو مسیر متفاوت‌اند
    /// و برنامه نمی‌داند کاربر با کدام‌یک آمده است.
    /// </summary>
    private void OnOpenUpdatePage(object? sender, RoutedEventArgs e)
    {
        if (_update is not null) App.OpenUrl(_update.ReleaseUrl);
    }

    private void OnStartWithSystemToggled(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var wanted = StartWithSystemBox.IsChecked == true;

        if (!Os.AutoStart.Set(wanted, out var problem))
        {
            Dialog.Info($"Start with {Os.DisplayName}", $"Could not change the startup entry: {problem}");

            _loading = true;
            StartWithSystemBox.IsChecked = !wanted;
            _loading = false;
            return;
        }

        _engine.Settings.StartWithWindows = wanted;
        // دست زدن به چک‌باکس یعنی جواب داده شده؛ دیگر موقع اجرا پرسیده نشود
        _engine.Settings.StartupPromptAnswered = true;
        _engine.SaveSettings();
    }

    /// <summary>
    /// فهرست از نو ساخته می‌شود، پس ردیف‌های قبلی دور انداخته می‌شوند و بایندینگِ چک‌باکسِ هرکدام
    /// باز می‌شود — که خودش <see cref="OnPluginToggled"/> را صدا می‌زند. تا وقتی کار این‌جا تمام
    /// نشده، آن هندلر باید ساکت بماند.
    /// </summary>
    private void Refresh()
    {
        _loading = true;
        try
        {
            PluginsList.ItemsSource = _engine.Plugins.Select(p => new PluginRow(p, _engine.Settings.IsEnabled(p.Id))).ToList();
            FooterText.Text = $"Plugins folder: {PluginPaths.UserPlugins}";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// نگهبانِ <c>_loading</c> اینجا از نگهبانِ بقیه‌ی چک‌باکس‌ها جدی‌تر است. رویداد
    /// <c>IsCheckedChanged</c> است، پس با تغییرِ برنامه‌ای هم می‌آید: وقتی <see cref="Refresh"/>
    /// فهرست را عوض می‌کند، Avalonia ردیف‌های قدیمی را پاک می‌کند، هر چک‌باکسِ بی‌بایندینگ به حالت
    /// اولیه برمی‌گردد و همین هندلر برای تک‌تکشان می‌افتد. بدون نگهبان، جوابِ هرکدام یک Refresh
    /// دیگر است وسط همان بازسازی — هم همه‌ی پلاگین‌ها خاموش می‌شوند، هم Avalonia کانتینری را
    /// می‌خواهد که دیگر نیست و ArgumentOutOfRange می‌دهد.
    /// </summary>
    private async void OnPluginToggled(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is not CheckBox { Tag: string pluginId } box) return;

        await _engine.SetEnabledAsync(pluginId, box.IsChecked == true);
        Refresh();
    }

    private void OnUninstall(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pluginId }) return;

        if (!Dialog.Confirm("Remove plugin", $"Remove plugin \"{pluginId}\" and all of its files?")) return;

        try
        {
            _engine.Uninstall(pluginId);
            Refresh();
        }
        catch (Exception ex)
        {
            Dialog.Info("Error", $"Could not remove: {ex.Message}");
        }
    }

    private void OnSaveHotkey(object? sender, RoutedEventArgs e)
    {
        _engine.Settings.Hotkey = (HotkeyBox.Text ?? string.Empty).Trim();
        _engine.SaveSettings();
        HotkeyStatus.Text = "Saved — takes effect after restart";
    }

    private void OnOpenPluginsFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PluginPaths.UserPlugins);
        Open(PluginPaths.UserPlugins);
    }

    private void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        if (!File.Exists(PluginPaths.LogFile))
        {
            Dialog.Info("Log", "Nothing has been logged yet.");
            return;
        }

        Open(PluginPaths.LogFile);
    }

    private static void Open(string path)
    {
        if (!Os.Opener.Open(path, out var problem)) Dialog.Info("PlugLauncher", $"Could not open {path}: {problem}");
    }

    /// <summary>
    /// راه خروج قابل دیدن. آیکن سینی هم منوی Exit دارد، ولی در ویندوز ۱۱ پیش‌فرض داخل بخش
    /// مخفی نوار وظیفه می‌نشیند و عملاً پیدا نمی‌شود.
    /// </summary>
    private void OnExitApp(object? sender, RoutedEventArgs e)
    {
        if (Dialog.Confirm("Exit", "Quit PlugLauncher? The hotkey will stop working until you start it again."))
            App.Quit();
    }

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        await _engine.ReloadAsync();
        Refresh();
    }

    /// <summary>ردیف نمایشی یک پلاگین در صفحه‌ی تنظیمات.</summary>
    private sealed class PluginRow(PluginDescriptor descriptor, bool isEnabled)
    {
        public string Id => descriptor.Id;
        public string Name => descriptor.Name;
        public string Version => $"v{descriptor.Manifest.Version}";
        public string Description => descriptor.Manifest.Description;
        public bool IsEnabled { get; set; } = isEnabled;
        public string? Error => descriptor.Error;

        public bool HasError => !string.IsNullOrWhiteSpace(descriptor.Error);

        public string StateText => descriptor.State switch
        {
            PluginState.Loaded => "Ready",
            PluginState.Disabled => "Disabled",
            PluginState.Failed => "Failed",
            PluginState.Conflicted => "Keyword taken",
            PluginState.Unsupported => "Not for this system",
            _ => "Not loaded"
        };

        public IBrush StateBrush => descriptor.State switch
        {
            PluginState.Loaded => new SolidColorBrush(Color.FromRgb(0x6D, 0xC7, 0x7A)),
            PluginState.Failed => new SolidColorBrush(Color.FromRgb(0xE0, 0x5B, 0x5B)),
            // نارنجی نه قرمز: پلاگین خراب نیست، فقط کلیدواژه‌اش گرفته شده — یا اصلاً مال اینجا نیست
            PluginState.Conflicted => new SolidColorBrush(Color.FromRgb(0xE0, 0x9B, 0x3D)),
            PluginState.Unsupported => new SolidColorBrush(Color.FromRgb(0xE0, 0x9B, 0x3D)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A))
        };
    }
}
