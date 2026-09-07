using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <summary>وضعیت یک پلاگین از دید میزبان.</summary>
public enum PluginState
{
    /// <summary>کشف شده ولی هنوز لود نشده.</summary>
    Discovered,

    /// <summary>لود و مقداردهی اولیه شده و آماده‌ی پاسخ به کوئری است.</summary>
    Loaded,

    /// <summary>کاربر آن را غیرفعال کرده است.</summary>
    Disabled,

    /// <summary>کامپایل یا اجرای اسکریپت شکست خورده؛ <see cref="PluginDescriptor.Error"/> دلیل را دارد.</summary>
    Failed,

    /// <summary>
    /// کلیدواژه‌اش را پلاگین قدیمی‌تری گرفته است. اصلاً لود نمی‌شود تا وقتی کلیدواژه‌اش عوض شود؛
    /// <see cref="PluginDescriptor.Error"/> می‌گوید با چه چیزی تعارض دارد.
    /// </summary>
    Conflicted
}

/// <summary>یک پلاگین کشف‌شده روی دیسک، به‌همراه نمونه‌ی لودشده‌اش (اگر لود شده باشد).</summary>
public sealed class PluginDescriptor
{
    public required PluginManifest Manifest { get; init; }

    /// <summary>مسیر مطلق پوشه‌ی پلاگین.</summary>
    public required string Directory { get; init; }

    /// <summary>مسیر مطلق فایل ورودی اسکریپت.</summary>
    public required string EntryFile { get; init; }

    /// <summary>
    /// زمان ساخت پوشه‌ی پلاگین. ملاکِ «قدیمی‌تر» وقتی دو پلاگین یک کلیدواژه را برداشته‌اند.
    /// نصب دوباره‌ی یک پلاگین پوشه را از نو می‌سازد، پس عملاً «زمان نصب» است نه زمان نگارش.
    /// </summary>
    public required DateTime InstalledAtUtc { get; init; }

    public PluginState State { get; internal set; } = PluginState.Discovered;

    /// <summary>پیام خطا در صورت <see cref="PluginState.Failed"/>.</summary>
    public string? Error { get; internal set; }

    public IPlugin? Instance { get; internal set; }

    internal IPluginContext? Context { get; set; }

    public string Id => Manifest.Id;
    public string Name => string.IsNullOrWhiteSpace(Manifest.Name) ? Manifest.Id : Manifest.Name;

    /// <summary>مسیر مطلق آیکن پلاگین، یا null اگر تعریف/موجود نباشد.</summary>
    public string? IconPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Manifest.Icon)) return null;
            var full = Path.IsPathRooted(Manifest.Icon)
                ? Manifest.Icon
                : Path.Combine(Directory, Manifest.Icon);
            return File.Exists(full) ? full : null;
        }
    }

    /// <summary>آیا این کوئری با یکی از کلیدواژه‌های پلاگین شروع می‌شود.</summary>
    public string? MatchKeyword(string raw)
    {
        var trimmed = raw.TrimStart();
        foreach (var keyword in Manifest.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;

            // "st" یا "st چیزی" — کلیدواژه باید کل کلمه‌ی اول باشد
            if (trimmed.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return keyword;
            if (trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase)) return keyword;
        }

        return null;
    }
}

/// <summary>
/// یک پلاگین که کلیدواژه‌اش را پلاگین قدیمی‌تری گرفته است، به‌همراه برنده و خود کلیدواژه.
/// </summary>
/// <param name="Loser">پلاگینی که کنار گذاشته شد.</param>
/// <param name="Winner">پلاگین قدیمی‌تری که کلیدواژه به آن رسید.</param>
/// <param name="Keyword">کلیدواژه‌ی مورد تعارض.</param>
public sealed record KeywordConflict(PluginDescriptor Loser, PluginDescriptor Winner, string Keyword);
