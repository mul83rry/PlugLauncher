using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PlugLauncher.Platform.Windows;

/// <summary>
/// اجرای خودکار هنگام ورود به ویندوز، از راه <c>HKCU\...\CurrentVersion\Run</c>.
///
/// عمداً HKCU و نه HKLM: نوشتن در HKLM دسترسی مدیر می‌خواهد و برنامه‌ای که بدون نصب و بدون UAC
/// اجرا می‌شود نباید برای یک چک‌باکس دنبال ارتقای دسترسی برود.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PlugLauncher";

    /// <summary>مسیر فایل اجرایی واقعی. برای اپ framework-dependent، <c>Assembly.Location</c> مسیر dll را می‌دهد نه exe.</summary>
    private static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>
    /// آیا ثبت شده و به <b>همین</b> فایل اجرایی اشاره می‌کند. برنامه قابل جابه‌جایی است و پوشه که
    /// جا‌به‌جا شود مقدار قبلی به مسیری اشاره می‌کند که دیگر وجود ندارد؛ چنین حالتی «ثبت‌نشده»
    /// حساب می‌شود تا دوباره و درست نوشته شود.
    /// </summary>
    public bool IsEnabled()
    {
        var registered = RegisteredPath();
        if (registered is null) return false;

        var current = ExecutablePath;
        return current is not null && string.Equals(Unquote(registered), current, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>مقدار خام ثبت‌شده، یا <c>null</c> اگر اصلاً ثبت نشده باشد.</summary>
    public static string? RegisteredPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public bool Set(bool enabled, out string problem) => enabled ? Enable(out problem) : Disable(out problem);

    private static bool Enable(out string problem)
    {
        var path = ExecutablePath;
        if (path is null)
        {
            problem = "the launcher could not work out its own path";
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            // مسیر داخل گیومه، وگرنه پوشه‌ای که فاصله دارد به چند آرگومان شکسته می‌شود
            key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            problem = "";
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static bool Disable(out string problem)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            problem = "";
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static string Unquote(string value)
        => value.Length > 1 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
