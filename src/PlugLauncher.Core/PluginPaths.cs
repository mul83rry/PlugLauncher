namespace PlugLauncher.Core;

/// <summary>مسیرهای ثابت برنامه: پوشه‌ی پلاگین‌ها، داده‌ها و کش کامپایل.</summary>
public static class PluginPaths
{
    /// <summary>ریشه‌ی داده‌های کاربر: <c>%APPDATA%\PlugLauncher</c>.</summary>
    public static string UserRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlugLauncher");

    /// <summary>پلاگین‌های نصب‌شده توسط کاربر (فروشگاه در فاز بعد اینجا می‌نویسد).</summary>
    public static string UserPlugins { get; } = Path.Combine(UserRoot, "plugins");

    /// <summary>پلاگین‌های همراه برنامه، کنار فایل اجرایی.</summary>
    public static string BundledPlugins { get; } = Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>کش اسمبلی‌های کامپایل‌شده‌ی اسکریپت‌ها.</summary>
    public static string ScriptCache { get; } = Path.Combine(UserRoot, "cache", "scripts");

    /// <summary>پوشه‌ی داده‌ی اختصاصی هر پلاگین.</summary>
    public static string DataFor(string pluginId) => Path.Combine(UserRoot, "data", Sanitize(pluginId));

    public static string SettingsFile { get; } = Path.Combine(UserRoot, "settings.json");
    public static string UsageFile { get; } = Path.Combine(UserRoot, "usage.json");
    public static string LogFile { get; } = Path.Combine(UserRoot, "logs", "plugLauncher.log");

    /// <summary>ریشه‌هایی که موقع کشف پلاگین اسکن می‌شوند؛ ریشه‌ی کاربر اولویت دارد.</summary>
    public static IEnumerable<string> PluginRoots()
    {
        yield return UserPlugins;
        if (!string.Equals(BundledPlugins, UserPlugins, StringComparison.OrdinalIgnoreCase))
            yield return BundledPlugins;
    }

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(UserPlugins);
        Directory.CreateDirectory(ScriptCache);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
