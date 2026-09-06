using System.IO;
using System.Windows;
using System.Windows.Forms;
using PlugLauncher.Core;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace PlugLauncher.App;

public partial class App : Application
{
    private const string InstanceMutexName = "PlugLauncher.SingleInstance";

    private Mutex? _instanceMutex;
    private NotifyIcon? _trayIcon;
    private PluginEngine? _engine;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("PlugLauncher is already running.", "PlugLauncher");
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var log = new FileLogger("app");
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error("unhandled exception", args.ExceptionObject as Exception);

        LoadUserTheme();

        _engine = new PluginEngine();
        _window = new MainWindow(_engine);

        // پنجره باید یک‌بار ساخته شود تا هندلش برای ثبت هات‌کی موجود باشد
        _window.Show();
        _window.Hide();

        CreateTrayIcon();

        await _engine.LoadAllAsync();
    }

    /// <summary>
    /// اگر کاربر فایل <c>%APPDATA%\PlugLauncher\theme\theme.xaml</c> را ساخته باشد،
    /// روی تم پیش‌فرض سوار می‌شود (فقط کلیدهایی که تعریف کرده override می‌شوند).
    /// </summary>
    private void LoadUserTheme()
    {
        var themeFile = Path.Combine(PluginPaths.UserRoot, "theme", "theme.xaml");
        if (!File.Exists(themeFile)) return;

        try
        {
            var dictionary = new ResourceDictionary { Source = new Uri(themeFile, UriKind.Absolute) };
            Resources.MergedDictionaries.Add(dictionary);
        }
        catch (Exception ex)
        {
            new FileLogger("theme").Error($"could not load user theme: {themeFile}", ex);
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => _window?.ShowLauncher());
        menu.Items.Add("Settings", null, (_, _) => _window?.OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "PlugLauncher",
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => _window?.ShowLauncher();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Usage.Flush();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
