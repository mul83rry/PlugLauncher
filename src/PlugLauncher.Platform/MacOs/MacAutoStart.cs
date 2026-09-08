using System.Runtime.Versioning;

namespace PlugLauncher.Platform.MacOs;

/// <summary>
/// اجرای خودکار روی مک: یک فایل plist در <c>~/Library/LaunchAgents</c>.
///
/// LaunchAgent و نه Login Item، چون Login Itemها از طریق یک API خصوصی یا AppleScript ثبت
/// می‌شوند؛ یک فایل که خودمان می‌نویسیم و خودمان پاک می‌کنیم قابل بازرسی و برگشت‌پذیر است.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAutoStart : IAutoStart
{
    private const string Label = "com.pluglauncher.launcher";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", Label + ".plist");

    public bool IsEnabled()
    {
        var path = Environment.ProcessPath;
        if (path is null || !File.Exists(PlistPath)) return false;

        // مثل ویندوز: اشاره به یک نسخه‌ی جابه‌جا شده یعنی «ثبت‌نشده»، تا از نو و درست نوشته شود
        try { return File.ReadAllText(PlistPath).Contains(path, StringComparison.Ordinal); }
        catch { return false; }
    }

    public bool Set(bool enabled, out string problem)
    {
        problem = "";

        try
        {
            if (!enabled)
            {
                if (File.Exists(PlistPath)) File.Delete(PlistPath);
                return true;
            }

            var path = Environment.ProcessPath;
            if (path is null)
            {
                problem = "the launcher could not work out its own path";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            File.WriteAllText(PlistPath, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key><string>{Label}</string>
                    <key>ProgramArguments</key><array><string>{path}</string></array>
                    <key>RunAtLoad</key><true/>
                </dict>
                </plist>

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
