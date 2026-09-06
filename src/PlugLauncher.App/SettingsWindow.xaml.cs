using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlugLauncher.Core;

namespace PlugLauncher.App;

public partial class SettingsWindow : Window
{
    private readonly PluginEngine _engine;

    public SettingsWindow(PluginEngine engine)
    {
        _engine = engine;
        InitializeComponent();

        HotkeyBox.Text = _engine.Settings.Hotkey;
        Refresh();
    }

    private void Refresh()
    {
        PluginsList.ItemsSource = _engine.Plugins.Select(p => new PluginRow(p, _engine.Settings.IsEnabled(p.Id))).ToList();
        FooterText.Text = $"پوشه‌ی پلاگین‌ها: {PluginPaths.UserPlugins}";
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
            $"پلاگین «{pluginId}» و همه‌ی فایل‌هایش حذف شود؟",
            "حذف پلاگین",
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
            MessageBox.Show($"حذف نشد: {ex.Message}", "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveHotkey(object sender, RoutedEventArgs e)
    {
        _engine.Settings.Hotkey = HotkeyBox.Text.Trim();
        _engine.SaveSettings();
        HotkeyStatus.Text = "ذخیره شد — بعد از راه‌اندازی مجدد اعمال می‌شود";
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
            MessageBox.Show("هنوز لاگی نوشته نشده است.", "لاگ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(PluginPaths.LogFile) { UseShellExecute = true });
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
            PluginState.Loaded => "آماده",
            PluginState.Disabled => "غیرفعال",
            PluginState.Failed => "خطا",
            _ => "لود نشده"
        };

        public Brush StateBrush => descriptor.State switch
        {
            PluginState.Loaded => new SolidColorBrush(Color.FromRgb(0x6D, 0xC7, 0x7A)),
            PluginState.Failed => new SolidColorBrush(Color.FromRgb(0xE0, 0x5B, 0x5B)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A))
        };
    }
}
