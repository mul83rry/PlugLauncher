using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.Windows;

/// <summary>
/// آیکنِ یک فایل اجرایی یا میان‌بر، به‌صورت پیکسل خام.
///
/// پیکسل خام و نه یک نوع تصویر، چون این پروژه هیچ کتابخانه‌ی گرافیکی‌ای نمی‌شناسد؛ ساختن تصویر
/// کارِ پوسته است. آیکن داخل فایل exe زندگی می‌کند و بیرون کشیدنش روی هر سیستم فرق دارد — روی
/// مک و لینوکس اصلاً چنین چیزی وجود ندارد، چون آنجا برنامه‌ها آیکنشان را در فایل جدا می‌گذارند.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsIcons : IIcons
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeNormal = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;

        // GetDIBits انتظار یک BITMAPINFO دارد که بعد از هدر جدول رنگ می‌آید؛ برای ۳۲ بیتی
        // خالی است ولی فضایش باید باشد.
        public uint quad0, quad1, quad2;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path, uint fileAttributes, ref ShFileInfo info, int size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, ref Bitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    public bool TryRead(string path, out int width, out int height, out byte[] pixels)
    {
        width = 0;
        height = 0;
        pixels = [];

        var info = new ShFileInfo();
        // UseFileAttributes یعنی «به فایل دست نزن، فقط از پسوندش تصمیم بگیر» — برای مسیری که
        // روی درایو شبکه‌ی خواب‌رفته است تفاوت بین آنی و چند ثانیه انتظار
        var attributes = File.Exists(path) ? 0u : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon | (attributes == 0 ? 0 : ShgfiUseFileAttributes);

        SHGetFileInfo(path, attributes, ref info, Marshal.SizeOf<ShFileInfo>(), flags);
        if (info.hIcon == IntPtr.Zero) return false;

        try
        {
            return TryPixels(info.hIcon, out width, out height, out pixels);
        }
        catch
        {
            return false;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static bool TryPixels(IntPtr icon, out int width, out int height, out byte[] pixels)
    {
        width = 0;
        height = 0;
        pixels = [];

        if (!GetIconInfo(icon, out var iconInfo)) return false;

        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero) return false;

            var bitmap = new Bitmap();
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<Bitmap>(), ref bitmap) == 0) return false;
            if (bitmap.bmWidth <= 0 || bitmap.bmHeight <= 0) return false;

            var header = new BitmapInfoHeader
            {
                biSize = 40,
                biWidth = bitmap.bmWidth,
                // ارتفاع منفی یعنی از بالا به پایین؛ وگرنه تصویر وارونه تحویل داده می‌شود
                biHeight = -bitmap.bmHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            var buffer = new byte[bitmap.bmWidth * bitmap.bmHeight * 4];
            var dc = GetDC(IntPtr.Zero);
            int copied;
            try
            {
                copied = GetDIBits(dc, iconInfo.hbmColor, 0, (uint)bitmap.bmHeight, buffer, ref header, 0);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            if (copied == 0) return false;

            // آیکن‌های قدیمی کانال آلفا ندارند و همه‌ی بایت‌هایش صفر می‌آید؛ بدون این، تصویر
            // کاملاً نامرئی می‌شود. ماسک را نمی‌خوانیم — یک مربع کامل از هیچ بهتر است.
            if (buffer.Where((_, i) => i % 4 == 3).All(a => a == 0))
                for (var i = 3; i < buffer.Length; i += 4)
                    buffer[i] = 255;

            width = bitmap.bmWidth;
            height = bitmap.bmHeight;
            pixels = buffer;
            return true;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }
}
