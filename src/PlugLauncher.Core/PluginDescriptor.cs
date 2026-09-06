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
    Failed
}

/// <summary>یک پلاگین کشف‌شده روی دیسک، به‌همراه نمونه‌ی لودشده‌اش (اگر لود شده باشد).</summary>
public sealed class PluginDescriptor
{
    public required PluginManifest Manifest { get; init; }

    /// <summary>مسیر مطلق پوشه‌ی پلاگین.</summary>
    public required string Directory { get; init; }

    /// <summary>مسیر مطلق فایل ورودی اسکریپت.</summary>
    public required string EntryFile { get; init; }

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
