using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlugLauncher.Core;

namespace PlugLauncher.App;

public partial class SettingsWindow : Window
{
    private readonly PluginEngine _engine;

    /// <summary>موقع پر کردن اولیه‌ی کنترل‌ها، هندلرها نباید چیزی بنویسند.</summary>
    private bool _loading;

    public SettingsWindow(PluginEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        Icon = LoadWindowIcon();

        _loading = true;
        HotkeyBox.Text = _engine.Settings.Hotkey;
        // از رجیستری خوانده می‌شود نه از settings.json، چون کاربر می‌تواند از Task Manager هم عوضش کند
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
        _loading = false;

        Refresh();
    }

    private void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var wanted = StartWithWindowsBox.IsChecked == true;

        if (!StartupRegistration.Apply(wanted))
        {
            MessageBox.Show(
                "Could not change the Windows startup entry. The log has the details.",
                "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);

            _loading = true;
            StartWithWindowsBox.IsChecked = !wanted;
            _loading = false;
            return;
        }

        _engine.Settings.StartWithWindows = wanted;
        // دست زدن به چک‌باکس یعنی جواب داده شده؛ دیگر موقع اجرا پرسیده نشود
        _engine.Settings.StartupPromptAnswered = true;
        _engine.SaveSettings();
    }

    /// <summary>
    /// این پنجره در تسک‌بار و alt-tab دیده می‌شود، پس آیکن لازم دارد؛ پنجره‌ی لانچر نه
    /// (<c>ShowInTaskbar=False</c> است و اصلاً چروم ندارد).
    /// </summary>
    private static ImageSource? LoadWindowIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "pluglauncher.ico");
            return File.Exists(path) ? BitmapFrame.Create(new Uri(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private void Refresh()
    {
        PluginsList.ItemsSource = _engine.Plugins.Select(p => new PluginRow(p, _engine.Settings.IsEnabled(p.Id))).ToList();
        FooterText.Text = $"Plugins folder: {PluginPaths.UserPlugins}";
    }

    private async void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string pluginId } box) return;

        await _engine.SetEnabledAsync(pluginId, box.IsChecked == true);
        Refresh();
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pluginId }) return;

        var confirm = MessageBox.Show(
            $"Remove plugin \"{pluginId}\" and all of its files?",
            "Remove plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _engine.Uninstall(pluginId);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveHotkey(object sender, RoutedEventArgs e)
    {
        _engine.Settings.Hotkey = HotkeyBox.Text.Trim();
        _engine.SaveSettings();
        HotkeyStatus.Text = "Saved — takes effect after restart";
    }

    private void OnOpenPluginsFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PluginPaths.UserPlugins);
        Process.Start(new ProcessStartInfo(PluginPaths.UserPlugins) { UseShellExecute = true });
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(PluginPaths.LogFile))
        {
            MessageBox.Show("Nothing has been logged yet.", "Log", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(PluginPaths.LogFile) { UseShellExecute = true });
    }

    /// <summary>
    /// راه خروج قابل دیدن. آیکن سینی هم منوی Exit دارد، ولی در ویندوز ۱۱ پیش‌فرض داخل بخش
    /// مخفی نوار وظیفه می‌نشیند و عملاً پیدا نمی‌شود.
    /// </summary>
    private void OnExitApp(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Quit PlugLauncher? The hotkey will stop working until you start it again.",
            "Exit", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes) Application.Current.Shutdown();
    }

    private async void OnReload(object sender, RoutedEventArgs e)
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

        public Visibility ErrorVisibility
            => string.IsNullOrWhiteSpace(descriptor.Error) ? Visibility.Collapsed : Visibility.Visible;

        public string StateText => descriptor.State switch
        {
            PluginState.Loaded => "Ready",
            PluginState.Disabled => "Disabled",
            PluginState.Failed => "Failed",
            _ => "Not loaded"
        };

        public Brush StateBrush => descriptor.State switch
        {
            PluginState.Loaded => new SolidColorBrush(Color.FromRgb(0x6D, 0xC7, 0x7A)),
            PluginState.Failed => new SolidColorBrush(Color.FromRgb(0xE0, 0x5B, 0x5B)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A))
        };
    }
}
