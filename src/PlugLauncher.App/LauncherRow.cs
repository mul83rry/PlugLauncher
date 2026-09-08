using System.Windows;
using System.Windows.Media;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>یک ردیف در لیست پنجره — یا نتیجه‌ی یک پلاگین است، یا خود پلاگین در حالت پیش‌فرض.</summary>
public sealed class LauncherRow
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public ImageSource? Icon { get; init; }

    /// <summary>وقتی ردیف، نتیجه‌ی یک کوئری است.</summary>
    public SearchItem? Item { get; init; }

    /// <summary>وقتی ردیف، خود پلاگین است (لیست حالت پیش‌فرض).</summary>
    public PluginDescriptor? Plugin { get; init; }

    public Visibility SubtitleVisibility
        => string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>سهم ردیف از یک کل (۰ تا ۱)، یا null وقتی نواری نباید کشیده شود.</summary>
    public double? Fraction { get; init; }

    public Visibility BarVisibility => Fraction is null ? Visibility.Collapsed : Visibility.Visible;

    // نوار با دو ستون ستاره‌ای کشیده می‌شود: سهم پرشده و باقی‌مانده. GridLength با ستاره‌ی صفر
    // ستون را جمع می‌کند، پس ۰ و ۱ هر دو بدون حالت خاص درست می‌افتند.
    private double Clamped => Math.Clamp(Fraction ?? 0, 0, 1);
    public GridLength BarWidth => new(Clamped, GridUnitType.Star);
    public GridLength BarRestWidth => new(1 - Clamped, GridUnitType.Star);

    public static LauncherRow FromResult(SearchItem item) => new()
    {
        Title = item.Title,
        Subtitle = item.Subtitle,
        Icon = IconLoader.Load(item.IconPath, item.Title),
        Fraction = item.Result.Fraction,
        Item = item
    };

    public static LauncherRow FromPlugin(PluginDescriptor plugin)
    {
        var keyword = plugin.Manifest.Keywords.FirstOrDefault();
        var description = string.IsNullOrWhiteSpace(plugin.Manifest.Description)
            ? plugin.Id
            : plugin.Manifest.Description;

        return new LauncherRow
        {
            Title = plugin.Name,
            Subtitle = keyword is null ? description : $"{keyword} — {description}",
            Icon = IconLoader.Load(plugin.IconPath, plugin.Name),
            Plugin = plugin
        };
    }
}
