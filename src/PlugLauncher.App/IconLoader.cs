using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PlugLauncher.Platform;

namespace PlugLauncher.App;

/// <summary>بارگذاری و کش آیکن ردیف‌ها: فایل تصویری، آیکن exe، و در نبود هردو یک آیکن حرف‌اول.</summary>
public static class IconLoader
{
    private const int Size = 84;

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Load(string? path, string fallbackText)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Cache.GetOrAdd("letter::" + FirstLetter(fallbackText), _ => CreateLetterIcon(fallbackText));

        return Cache.GetOrAdd(path, LoadFromFile) ?? CreateLetterIcon(fallbackText);
    }

    private static Bitmap? LoadFromFile(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return FromExecutable(path);

            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToHeight(stream, Size);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// آیکنی که داخل خود فایل اجرایی نشسته. بیرون کشیدنش کارِ سیستم‌عامل است و روی مک و لینوکس
    /// اصلاً چنین چیزی وجود ندارد؛ آنجا آیکن حرف‌اول جایش را می‌گیرد.
    /// </summary>
    private static Bitmap? FromExecutable(string path)
    {
        if (!Os.Icons.TryRead(path, out var width, out var height, out var pixels)) return null;

        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul,
                pinned.AddrOfPinnedObject(),
                new PixelSize(width, height),
                new Vector(96, 96),
                width * 4);
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>آیکن جایگزین: حرف اول عنوان روی یک مربع گرد.</summary>
    private static Bitmap CreateLetterIcon(string text)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));

        using (var context = bitmap.CreateDrawingContext())
        {
            var background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            context.DrawRectangle(background, null, new RoundedRect(new Rect(0, 0, Size, Size), 16));

            var formatted = new FormattedText(
                FirstLetter(text),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                42,
                new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)));

            context.DrawText(formatted, new Point((Size - formatted.Width) / 2, (Size - formatted.Height) / 2));
        }

        return bitmap;
    }

    private static string FirstLetter(string text)
        => string.IsNullOrWhiteSpace(text) ? "?" : text.Trim()[..1].ToUpperInvariant();
}
