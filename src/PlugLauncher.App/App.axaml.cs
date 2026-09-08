using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using PlugLauncher.Core;
using PlugLauncher.Platform;

namespace PlugLauncher.App;

public partial class App : Application
{
    private readonly SingleInstance _instance = new();

    private TrayIcon? _trayIcon;
    private PluginEngine? _engine;
    private MainWindow? _window;
    private ClipboardBridge? _clipboard;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) => Cleanup();

        // بالا آمدن پشت شروعِ حلقه‌ی پیام صف می‌شود، نه قبلش: اولین کاری که ممکن است لازم شود
        // نشان دادن یک پنجره است، و پنجره بدون حلقه‌ی پیام جایی نمی‌رود.
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await StartAsync(desktop);
            }
            catch (Exception ex)
            {
                // یک Task رهاشده خطایش را جایی نمی‌گوید؛ برنامه‌ای که نصفه بالا آمده باید
                // دست‌کم در لاگ بگوید کجا ایستاده
                new FileLogger("app").Error("could not finish starting up", ex);
            }
        });
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!_instance.TryAcquire())
        {
            Dialog.Info("PlugLauncher", "PlugLauncher is already running.");
            desktop.Shutdown();
            return;
        }

        var log = new FileLogger("app");

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            log.Error("unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            log.Error("unhandled exception", args.ExceptionObject as Exception);

        LoadUserTheme();

        _engine = new PluginEngine();
        _window = new MainWindow(_engine);

        // پنجره باید یک‌بار ساخته شود تا هرچه به سیستم پنجره‌ها وصل است — کلیپ‌بورد، نمایشگرها،
        // خودِ رندرر — آماده باشد و اولین فشردنِ هات‌کی معطل نماند
        _window.Show();
        _window.Hide();

        _clipboard = new ClipboardBridge(_window, log);
        _clipboard.Install();
        _window.UseClipboard(_clipboard);

        Contracts.Ask.Use(Dialog.Confirm);

        // شکستش بی‌صدا نیست ولی جلوی پلاگین را هم نمی‌گیرد: پلاگین درخواست می‌دهد،
        // تصمیم با سیستم است
        Contracts.Shell.Use(
            target =>
            {
                if (!Os.Opener.Open(target, out var problem)) log.Warn($"could not open \"{target}\": {problem}");
            },
            path =>
            {
                if (!Os.Opener.Reveal(path, out var problem)) log.Warn($"could not show \"{path}\": {problem}");
            });

        CreateTrayIcon(desktop, log);

        await _engine.LoadAllAsync();

        WarnAboutKeywordConflicts();

        // بعد از لود پلاگین‌ها، چون این یکی می‌تواند یک پنجره‌ی سؤال جلوی کاربر بگذارد و قبلش
        // استارتاپ را نگه می‌داشت
        SyncStartupRegistration();

        // انتظارش کشیده نمی‌شود: اگر شبکه کند باشد نباید استارتاپ را نگه دارد
        _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// بررسی نسخه‌ی جدید در پس‌زمینه، حداکثر روزی یک‌بار. نتیجه دو نشانه دارد: یک پیام گوشه‌ی
    /// صفحه که کلیک رویش صفحه‌ی ریلیز را باز می‌کند، و یک نقطه روی دکمه‌ی Settings که تا بسته شدن
    /// برنامه می‌ماند — چون آن پیام چند ثانیه بیشتر نمی‌ماند و ممکن است اصلاً دیده نشود.
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

        var update = result.Update;
        Toast.Show(
            $"PlugLauncher {update.Version.ToString(3)} is available",
            $"You are running {UpdateChecker.RunningVersion.ToString(3)}. Click to open the download page.",
            () => OpenUrl(update.ReleaseUrl));
    }

    internal static void OpenUrl(string url)
    {
        if (!Os.Opener.Open(url, out var problem))
            new FileLogger("app").Warn($"could not open {url}: {problem}");
    }

    /// <summary>خروج از برنامه، از هرجا که خواسته شود.</summary>
    internal static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    /// <summary>
    /// تعارض کلیدواژه ساکت می‌ماند وگرنه: پلاگین هست، لود نشده، و کاربر فقط می‌بیند تایپ کردن
    /// کلیدواژه کار نمی‌کند. یک پیام گوشه‌ی صفحه به‌جای یک دیالوگ، چون این وضعیت تا عوض شدن
    /// کلیدواژه می‌ماند و یک پنجره‌ی مودال در هر بار اجرا آزاردهنده می‌شد. جزئیاتش در تنظیمات هست.
    /// </summary>
    private void WarnAboutKeywordConflicts()
    {
        var conflicts = _engine?.KeywordConflicts;
        if (conflicts is null || conflicts.Count == 0) return;

        var first = conflicts[0];
        var text = conflicts.Count == 1
            ? $"\"{first.Loser.Name}\" is off: its keyword \"{first.Keyword}\" already belongs to \"{first.Winner.Name}\"."
            : $"{conflicts.Count} plugins are off because their keywords are already taken, starting with \"{first.Loser.Name}\" (\"{first.Keyword}\").";

        Toast.Show("PlugLauncher — keyword conflict", text + " Open settings for details.");
    }

    /// <summary>
    /// ثبتِ اجرای خودکار را با تنظیمات هماهنگ می‌کند و اگر روشن نیست، یک‌بار از کاربر می‌پرسد.
    /// </summary>
    private void SyncStartupRegistration()
    {
        if (_engine is null) return;

        var log = new FileLogger("startup");
        var settings = _engine.Settings;

        if (Os.AutoStart.IsEnabled())
        {
            // کاربر ممکن است از خود سیستم‌عامل روشنش کرده باشد
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
            if (Enable(log)) return;

            settings.StartWithWindows = false;
            _engine.SaveSettings();
            return;
        }

        if (settings.StartupPromptAnswered) return;

        var answer = Dialog.Confirm(
            "PlugLauncher",
            $"Start PlugLauncher automatically when you sign in to {Os.DisplayName}?\n\n" +
            "The hotkey only works while PlugLauncher is running. You can change this later in settings.");

        // چه بله چه خیر، دیگر پرسیده نمی‌شود؛ چک‌باکس تنظیمات جای تغییر نظر است
        settings.StartupPromptAnswered = true;
        settings.StartWithWindows = answer && Enable(log);
        _engine.SaveSettings();
    }

    private static bool Enable(FileLogger log)
    {
        if (Os.AutoStart.Set(true, out var problem))
        {
            log.Info($"start with {Os.Name} enabled");
            return true;
        }

        log.Warn($"could not enable start with {Os.Name}: {problem}");
        return false;
    }

    /// <summary>
    /// اگر کاربر فایل <c>theme.json</c> را در پوشه‌ی داده‌هایش ساخته باشد، رنگ‌های تم پیش‌فرض را
    /// عوض می‌کند. فقط رنگ، و فقط با همان کلیدهایی که در <c>Theme.axaml</c> هست.
    ///
    /// قبلاً یک فایل XAML بود که مستقیم به موتور تمِ WPF داده می‌شد؛ آن موتور دیگر اینجا نیست و
    /// XAMLش را هیچ‌کس نمی‌خواند. یک جدولِ «کلید: رنگ» همان کار را می‌کند و روی هر سه سیستم
    /// یکسان خوانده می‌شود.
    /// </summary>
    private void LoadUserTheme()
    {
        var themeFile = Path.Combine(PluginPaths.UserRoot, "theme", "theme.json");
        if (!File.Exists(themeFile)) return;

        try
        {
            var colors = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(themeFile));
            if (colors is null) return;

            foreach (var (key, value) in colors)
            {
                if (Color.TryParse(value, out var color)) Resources[key] = new SolidColorBrush(color);
                else new FileLogger("theme").Warn($"\"{value}\" is not a colour ({key})");
            }
        }
        catch (Exception ex)
        {
            new FileLogger("theme").Error($"could not load user theme: {themeFile}", ex);
        }
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, FileLogger log)
    {
        var show = new NativeMenuItem("Show");
        show.Click += (_, _) => _window?.ShowLauncher();

        var settings = new NativeMenuItem("Settings");
        settings.Click += (_, _) => _window?.OpenSettings();

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Add(show);
        menu.Add(settings);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        _trayIcon = new TrayIcon
        {
            Icon = LoadAppIcon(log),
            ToolTipText = "PlugLauncher",
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => _window?.ShowLauncher();
    }

    /// <summary>
    /// آیکن برنامه از فایل کنار exe. PNG و نه ICO: فرمت ico مال ویندوز است و فقط خودِ فایل
    /// اجرایی و نصاب به آن نیاز دارند.
    /// </summary>
    internal static WindowIcon? LoadAppIcon(FileLogger? log = null)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "pluglauncher.png");
            if (File.Exists(path)) return new WindowIcon(path);

            log?.Warn($"the application icon is missing: {path}");
        }
        catch (Exception ex)
        {
            log?.Warn($"could not load the application icon: {ex.Message}");
        }

        return null;
    }

    private void Cleanup()
    {
        _engine?.Usage.Flush();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _instance.Dispose();
    }
}
