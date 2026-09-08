using Avalonia;
using Avalonia.Controls;

namespace PlugLauncher.App;

internal static class Program
{
    /// <summary>
    /// نقطه‌ی شروع. WPF این را خودش تولید می‌کرد و پنهان بود؛ اینجا صریح است، چون
    /// <c>UsePlatformDetect</c> همان جایی است که تصمیم می‌گیرد پشتِ پنجره‌ها Win32 باشد یا
    /// X11 یا Cocoa.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
