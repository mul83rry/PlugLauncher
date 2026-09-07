using Microsoft.Win32;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>
/// اجرای خودکار هنگام ورود به ویندوز، از راه <c>HKCU\...\CurrentVersion\Run</c>.
/// عمداً HKCU و نه HKLM: نوشتن در HKLM دسترسی مدیر می‌خواهد و برنامه‌ای که بدون نصب و
/// بدون UAC اجرا می‌شود نباید برای یک چک‌باکس دنبال ارتقای دسترسی برود.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PlugLauncher";

    private static readonly FileLogger Log = new("startup");

    /// <summary>مسیر فایل اجرایی واقعی. برای اپ framework-dependent، <c>Assembly.Location</c> مسیر dll را می‌دهد نه exe.</summary>
    private static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>
    /// آیا ثبت شده و به <b>همین</b> فایل اجرایی اشاره می‌کند. برنامه قابل جابه‌جایی است و پوشه که
    /// جا‌به‌جا شود مقدار قبلی به مسیری اشاره می‌کند که دیگر وجود ندارد؛ چنین حالتی «ثبت‌نشده»
    /// حساب می‌شود تا دوباره و درست نوشته شود.
    /// </summary>
    public static bool IsEnabled()
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
        catch (Exception ex)
        {
            Log.Warn($"could not read the Run key: {ex.Message}");
            return null;
        }
    }

    /// <summary>در صورت موفقیت <c>true</c>. خطا فقط لاگ می‌شود؛ نبودن اجرای خودکار دلیل کرش نیست.</summary>
    public static bool Enable()
    {
        var path = ExecutablePath;
        if (path is null)
        {
            Log.Warn("could not resolve the executable path; start with Windows was not enabled");
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            // مسیر داخل گیومه، وگرنه پوشه‌ای که فاصله دارد به چند آرگومان شکسته می‌شود
            key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            Log.Info($"start with Windows enabled: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("could not write the Run key", ex);
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("start with Windows disabled");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("could not remove the Run key value", ex);
            return false;
        }
    }

    public static bool Apply(bool enabled) => enabled ? Enable() : Disable();

    private static string Unquote(string value)
        => value.Length > 1 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
