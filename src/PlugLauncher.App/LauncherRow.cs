using Avalonia.Media.Imaging;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>یک ردیف در لیست پنجره — یا نتیجه‌ی یک پلاگین است، یا خود پلاگین در حالت پیش‌فرض.</summary>
public sealed class LauncherRow
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;
    public Bitmap? Icon { get; init; }

    /// <summary>وقتی ردیف، نتیجه‌ی یک کوئری است.</summary>
    public SearchItem? Item { get; init; }

    /// <summary>وقتی ردیف، خود پلاگین است (لیست حالت پیش‌فرض).</summary>
    public PluginDescriptor? Plugin { get; init; }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    /// <summary>متن نمای بلند ردیف؛ null یعنی ردیف نما ندارد.</summary>
    public string? DetailText { get; init; }

    /// <summary>عنوان نما؛ اگر خالی باشد عنوان خود ردیف.</summary>
    public string? DetailTitle { get; init; }

    public bool HasDetail => !string.IsNullOrEmpty(DetailText);

    /// <summary>سهم ردیف از یک کل (۰ تا ۱)، یا null وقتی نواری نباید کشیده شود.</summary>
    public double? Fraction { get; init; }

    public bool HasBar => Fraction is not null;

    /// <summary>همان سهم، ولی همیشه بین ۰ و ۱ — پلاگین می‌تواند عدد بی‌ربط بدهد.</summary>
    public double BarShare => Math.Clamp(Fraction ?? 0, 0, 1);

    public static LauncherRow FromResult(SearchItem item) => new()
    {
        Title = item.Title,
        Subtitle = item.Subtitle,
        Icon = IconLoader.Load(item.IconPath, item.Title),
        Fraction = item.Result.Fraction,
        DetailText = item.Result.DetailText,
        DetailTitle = item.Result.DetailTitle,
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
