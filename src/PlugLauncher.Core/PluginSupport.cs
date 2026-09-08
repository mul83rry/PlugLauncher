namespace PlugLauncher.Core;

/// <summary>
/// آیا یک پلاگین روی این سیستم و این نسخه از لانچر اصلاً می‌تواند کار کند.
///
/// یک جا، چون دو نفر این را می‌پرسند و باید یک جواب بدهند: موتور موقع لود، و فروشگاه قبل از
/// اینکه دکمه‌ی Install را فعال کند. نصب کردن چیزی که همان لحظه شکست می‌خورد بدترین حالت است.
/// </summary>
public static class PluginSupport
{
    /// <summary>نام این سیستم‌عامل با همان املایی که در <c>plugin.json</c> نوشته می‌شود.</summary>
    public static string ThisPlatform { get; } =
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : "unknown";

    /// <summary>
    /// دلیلِ اینکه این پلاگین اینجا کار نمی‌کند، یا <c>null</c> وقتی کار می‌کند. متن همان چیزی است
    /// که به کاربر نشان داده می‌شود، پس به زبان آدمیزاد است نه کد.
    /// </summary>
    public static string? Reject(IReadOnlyList<string>? platforms, string? minCore)
    {
        if (platforms is { Count: > 0 }
            && !platforms.Any(p => string.Equals(p.Trim(), ThisPlatform, StringComparison.OrdinalIgnoreCase)))
        {
            return $"made for {string.Join(" and ", platforms.Select(Pretty))}, not for {Pretty(ThisPlatform)}";
        }

        if (!string.IsNullOrWhiteSpace(minCore)
            && UpdateChecker.TryParseVersion(minCore, out var needed)
            && UpdateChecker.RunningVersion < needed)
        {
            return $"needs PlugLauncher {minCore.Trim()} or later; this is {Running()}";
        }

        return null;
    }

    private static string Running()
    {
        var version = UpdateChecker.RunningVersion;
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string Pretty(string platform) => platform.Trim().ToLowerInvariant() switch
    {
        "windows" => "Windows",
        "macos" => "macOS",
        "linux" => "Linux",
        _ => platform.Trim()
    };
}
