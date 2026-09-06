using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <summary>یک ردیف نتیجه به‌همراه پلاگین صاحبش؛ چیزی که UI مستقیماً نمایش می‌دهد.</summary>
public sealed class SearchItem
{
    public required PluginDescriptor Plugin { get; init; }
    public required PluginResult Result { get; init; }

    /// <summary>امتیاز نهایی بعد از اعمال تقویت آمار استفاده.</summary>
    public int Score { get; internal set; }

    public string Title => Result.Title;
    public string Subtitle => Result.Subtitle;

    /// <summary>کلید پایدار برای ثبت آمار استفاده.</summary>
    public string UsageKey => $"{Plugin.Id}::{Result.Id ?? Result.Title}";

    /// <summary>مسیر مطلق آیکن؛ اگر ردیف آیکن نداشته باشد، آیکن خود پلاگین.</summary>
    public string? IconPath
    {
        get
        {
            var icon = Result.IconPath;
            if (string.IsNullOrWhiteSpace(icon)) return Plugin.IconPath;

            var full = Path.IsPathRooted(icon) ? icon : Path.Combine(Plugin.Directory, icon);
            return File.Exists(full) ? full : Plugin.IconPath;
        }
    }
}
