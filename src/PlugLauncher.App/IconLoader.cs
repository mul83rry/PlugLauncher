using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlugLauncher.App;

/// <summary>بارگذاری و کش آیکن ردیف‌ها: فایل تصویری، آیکن exe، و در نبود هردو یک آیکن حرف‌اول.</summary>
public static class IconLoader
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string? path, string fallbackText)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Cache.GetOrAdd("letter::" + FirstLetter(fallbackText), _ => CreateLetterIcon(fallbackText));

        return Cache.GetOrAdd(path, LoadFromFile) ?? CreateLetterIcon(fallbackText);
    }

    private static ImageSource? LoadFromFile(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return ExtractExeIcon(path);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelHeight = 84;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractExeIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>آیکن جایگزین: حرف اول عنوان روی یک مربع گرد.</summary>
    private static ImageSource CreateLetterIcon(string text)
    {
        const int size = 84;
        var letter = FirstLetter(text);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            context.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), 16, 16);

            var formatted = new FormattedText(
                letter,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                42,
                new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
                1.0);

            context.DrawText(formatted, new Point((size - formatted.Width) / 2, (size - formatted.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string FirstLetter(string text)
        => string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[..1].ToUpperInvariant();
}
