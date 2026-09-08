using System.Runtime.Versioning;

namespace PlugLauncher.Platform.Linux;

/// <summary>
/// اجرای خودکار روی لینوکس: یک فایل <c>.desktop</c> در <c>~/.config/autostart</c>.
///
/// این همان قراردادی است که XDG تعریف کرده و گنوم، کی‌دی‌ای و بقیه‌ی دسکتاپ‌ها رعایتش
/// می‌کنند — تنها راهی که به یک دسکتاپ خاص وابسته نیست.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxAutoStart : IAutoStart
{
    private static string DesktopFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "pluglauncher.desktop");

    public bool IsEnabled()
    {
        var path = Environment.ProcessPath;
        if (path is null || !File.Exists(DesktopFile)) return false;

        try { return File.ReadAllText(DesktopFile).Contains(path, StringComparison.Ordinal); }
        catch { return false; }
    }

    public bool Set(bool enabled, out string problem)
    {
        problem = "";

        try
        {
            if (!enabled)
            {
                if (File.Exists(DesktopFile)) File.Delete(DesktopFile);
                return true;
            }

            var path = Environment.ProcessPath;
            if (path is null)
            {
                problem = "the launcher could not work out its own path";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
            File.WriteAllText(DesktopFile, $"""
                [Desktop Entry]
                Type=Application
                Name=PlugLauncher
                Exec="{path}"
                Terminal=false
                X-GNOME-Autostart-enabled=true

                """);

            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }
}
