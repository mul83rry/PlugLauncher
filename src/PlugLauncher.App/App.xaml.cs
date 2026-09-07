using System.Diagnostics;
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

    /// <summary>آدرسی که کلیک روی بالن باز می‌کند. فقط بالنِ به‌روزرسانی آن را ست می‌کند.</summary>
    private string? _updateUrl;

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

        WarnAboutKeywordConflicts();

        // بعد از لود پلاگین‌ها، چون MessageBox رشته را نگه می‌دارد و قبلش استارتاپ را کند می‌کرد
        SyncStartupRegistration();

        // انتظارش کشیده نمی‌شود: اگر شبکه کند باشد نباید استارتاپ را نگه دارد
        _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// بررسی نسخه‌ی جدید در پس‌زمینه، حداکثر روزی یک‌بار. نتیجه دو نشانه دارد: یک بالن سینی که
    /// کلیک رویش صفحه‌ی ریلیز را باز می‌کند، و یک نقطه روی دکمه‌ی Settings که تا بسته شدن برنامه
    /// می‌ماند — چون بالن چند ثانیه بیشتر نمی‌ماند و ممکن است اصلاً دیده نشود.
    ///
    /// چیزی خودکار دانلود یا نصب نمی‌شود؛ نصب دست کاربر است.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        if (_engine is null) return;

        var settings = _engine.Settings;
        if (!settings.CheckForUpdates) return;

        var last = settings.LastUpdateCheckUtc;
        if (last is not null && DateTime.UtcNow - last.Value < UpdateChecker.AutomaticInterval) return;

        using var checker = new UpdateChecker(UpdateChecker.RunningVersion);
        var result = await checker.CheckAsync();

        // بررسی ناموفق ثبت نمی‌شود تا اجرای بعدی دوباره امتحان کند
        if (!result.Succeeded) return;

        settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _engine.SaveSettings();

        if (result.Update is null) return;

        _window?.MarkUpdateAvailable(result.Update);
        ShowUpdateBalloon(result.Update);
    }

    private void ShowUpdateBalloon(UpdateInfo update)
    {
        if (_trayIcon is null) return;

        _updateUrl = update.ReleaseUrl;

        _trayIcon.ShowBalloonTip(
            10000,
            $"PlugLauncher {update.Version.ToString(3)} is available",
            $"You are running {UpdateChecker.RunningVersion.ToString(3)}. Click to open the download page.",
            ToolTipIcon.Info);
    }

    /// <summary>
    /// کلیک روی بالن. بالن‌های دیگر (تعارض کلیدواژه) آدرسی ست نمی‌کنند، پس کلیک رویشان کاری
    /// نمی‌کند؛ و بسته شدن بالن آدرس را پاک می‌کند تا کلیک روی بالن بعدی چیز اشتباهی باز نکند.
    /// </summary>
    private void OpenUpdatePage()
    {
        var url = _updateUrl;
        if (string.IsNullOrEmpty(url)) return;

        _updateUrl = null;
        OpenUrl(url);
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            new FileLogger("app").Warn($"could not open {url}: {ex.Message}");
        }
    }

    /// <summary>
    /// تعارض کلیدواژه ساکت می‌ماند وگرنه: پلاگین هست، لود نشده، و کاربر فقط می‌بیند تایپ کردن
    /// کلیدواژه کار نمی‌کند. بالن سینی به‌جای MessageBox، چون این وضعیت تا عوض شدن کلیدواژه
    /// می‌ماند و یک دیالوگ مودال در هر بار اجرا آزاردهنده می‌شد. جزئیاتش در تنظیمات هست.
    /// </summary>
    private void WarnAboutKeywordConflicts()
    {
        var conflicts = _engine?.KeywordConflicts;
        if (conflicts is null || conflicts.Count == 0 || _trayIcon is null) return;

        var first = conflicts[0];
        var text = conflicts.Count == 1
            ? $"\"{first.Loser.Name}\" is off: its keyword \"{first.Keyword}\" already belongs to \"{first.Winner.Name}\"."
            : $"{conflicts.Count} plugins are off because their keywords are already taken, starting with \"{first.Loser.Name}\" (\"{first.Keyword}\").";

        _trayIcon.ShowBalloonTip(
            10000,
            "PlugLauncher — keyword conflict",
            text + " Open settings for details.",
            ToolTipIcon.Warning);
    }

    /// <summary>
    /// رجیستری را با تنظیمات هماهنگ می‌کند و اگر اجرای خودکار روشن نیست، یک‌بار از کاربر می‌پرسد.
    /// </summary>
    private void SyncStartupRegistration()
    {
        if (_engine is null) return;

        var settings = _engine.Settings;

        if (StartupRegistration.IsEnabled())
        {
            // کاربر ممکن است از خود ویندوز (Task Manager → Startup) روشنش کرده باشد
            if (settings.StartWithWindows) return;

            settings.StartWithWindows = true;
            settings.StartupPromptAnswered = true;
            _engine.SaveSettings();
            return;
        }

        // روشن بوده ولی الان نیست: یا پوشه جابه‌جا شده و مقدار قبلی به مسیر مرده اشاره می‌کند،
        // یا کسی از بیرون پاکش کرده. بی‌صدا دوباره نوشته می‌شود، چون کاربر قبلاً «بله» گفته.
        if (settings.StartWithWindows)
        {
            if (StartupRegistration.Enable()) return;

            settings.StartWithWindows = false;
            _engine.SaveSettings();
            return;
        }

        if (settings.StartupPromptAnswered) return;

        var answer = MessageBox.Show(
            "Start PlugLauncher automatically when you sign in to Windows?\n\n" +
            "The hotkey only works while PlugLauncher is running. You can change this later in settings.",
            "PlugLauncher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        // چه بله چه خیر، دیگر پرسیده نمی‌شود؛ چک‌باکس تنظیمات جای تغییر نظر است
        settings.StartupPromptAnswered = true;
        settings.StartWithWindows = answer == MessageBoxResult.Yes && StartupRegistration.Enable();
        _engine.SaveSettings();
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
            Icon = LoadAppIcon(),
            Text = "PlugLauncher",
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => _window?.ShowLauncher();
        _trayIcon.BalloonTipClicked += (_, _) => OpenUpdatePage();
        _trayIcon.BalloonTipClosed += (_, _) => _updateUrl = null;
    }

    /// <summary>
    /// آیکن برنامه از فایل کنار exe. اندازه صریحاً کوچک خواسته می‌شود تا ویندوز فریم ۱۶ را
    /// بردارد و آیکن ۳۲ را کوچک نکند. اگر فایل نبود یا خوانده نشد، آیکن پیش‌فرض ویندوز.
    /// </summary>
    internal static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "pluglauncher.ico");
            if (File.Exists(path)) return new System.Drawing.Icon(path, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            new FileLogger("app").Warn($"could not load the application icon: {ex.Message}");
        }

        return System.Drawing.SystemIcons.Application;
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
