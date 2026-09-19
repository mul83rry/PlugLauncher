using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using PlugLauncher.Core;

namespace PlugLauncher.App;

/// <summary>
/// کلیپ‌بورد سیستم، آن‌طور که یک پلاگین می‌تواند از آن استفاده کند.
///
/// دو ناهماهنگی اینجا حل می‌شود: کلیپ‌بوردِ کتابخانه‌ی پنجره‌ها فقط ناهم‌زمان جواب می‌دهد و فقط
/// از ترد UI قابل صدا زدن است، ولی <see cref="Contracts.Clipboard"/> هم‌زمان است و از هر تردی
/// صدا زده می‌شود — چون امضایی که پلاگین‌ها می‌بینند باید ساده بماند.
/// </summary>
internal sealed class ClipboardBridge(TopLevel host, FileLogger log)
{
    /// <summary>سقف انتظار برای یک خواندن. کلیپ‌بوردی که جواب نمی‌دهد نباید پلاگین را بخواباند.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    /// <summary>آخرین متنی که خوانده‌ایم؛ جوابِ صداکننده‌ای که خودش روی ترد UI است.</summary>
    private string _text = string.Empty;

    public void Install() => Contracts.Clipboard.Use(Copy, Read, ReadImage);

    /// <summary>
    /// هربار که پنجره باز می‌شود صدا زده می‌شود. ترتیب کار کاربر همیشه یکی است — چیزی را کپی
    /// می‌کند و بعد لانچر را باز می‌کند — پس همین یک نقطه کافی است تا نسخه‌ی نگه‌داشته تازه باشد.
    /// </summary>
    public void Refresh() => Dispatcher.UIThread.Post(() => _ = ReadAsync());

    private void Copy(string text)
    {
        var value = text ?? string.Empty;
        _text = value;

        Dispatcher.UIThread.Post(() => _ = WriteAsync(value));
    }

    private string Read()
    {
        // روی ترد UI نمی‌شود منتظر ماند — همان ترد است که باید جواب را بیاورد. نسخه‌ای که موقع
        // باز شدن پنجره خوانده شده همان چیزی است که کاربر یک لحظه پیش کپی کرده.
        if (Dispatcher.UIThread.CheckAccess()) return _text;

        var answer = new TaskCompletionSource<string>();
        Dispatcher.UIThread.Post(async () => answer.TrySetResult(await ReadAsync()));

        return answer.Task.Wait(Patience) ? answer.Task.Result : _text;
    }

    /// <summary>همیشه روی ترد UI: کلیپ‌بورد ویندوز فقط از تردی که پنجره‌ها را دارد جواب می‌دهد.</summary>
    private async Task<string> ReadAsync()    {
        var clipboard = host.Clipboard;
        if (clipboard is null) return _text;

        try
        {
            _text = await clipboard.TryGetTextAsync() ?? string.Empty;
        }
        catch (Exception ex)
        {
            log.Warn($"could not read the clipboard: {ex.Message}");
        }

        return _text;
    }

    private async Task WriteAsync(string text)
    {
        var clipboard = host.Clipboard;
        if (clipboard is null)
        {
            log.Warn("could not put text on the clipboard: there is no clipboard");
            return;
        }

        // کلیپ‌بورد گاهی دست برنامه‌ی دیگری است و همان لحظه جواب نمی‌دهد؛ همان حلقه‌ی «سه بار
        // تلاش» که قبلاً در هفت اسکریپت کپی شده بود، حالا فقط اینجاست.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await clipboard.SetTextAsync(text);
                return;
            }
            catch
            {
                await Task.Delay(40);
            }
        }

        log.Warn("could not put text on the clipboard");
    }

    // ===== تصویر روی کلیپ‌بورد =====

    private Contracts.ClipboardImage? ReadImage()
    {
        // همان قصه‌ی Read: روی ترد UI نمی‌شود منتظر ماند، پس کار به همان ترد سپرده می‌شود
        if (Dispatcher.UIThread.CheckAccess()) return ReadImageWin32();

        var answer = new TaskCompletionSource<Contracts.ClipboardImage?>();
        Dispatcher.UIThread.Post(() => answer.TrySetResult(ReadImageWin32()));

        return answer.Task.Wait(Patience) ? answer.Task.Result : null;
    }

    private const uint CfDib = 8;
    private const uint CfBitmap = 2;

    /// <summary>
    /// تصویر کلیپ‌بورد ویندوز استانداردش DIB است: یک BITMAPINFOHEADER و بعد پیکسل‌ها — همان چیزی
    /// که مرورگر و ابزار اسکرین‌شات می‌گذارند. بعضی برنامه‌ها فقط HBITMAP می‌گذارند؛ آن هم با
    /// GetDIBits به DIB برمی‌گردد و همان یک مسیر ادامه می‌دهد. Avalonia خواندن هیچ‌کدام را نمی‌دهد،
    /// پس همین‌جا باز می‌کنیم — ویندوزی است و روی چیز دیگری null برمی‌گردد، همان‌طور که قرارداد گفته.
    /// </summary>
    private Contracts.ClipboardImage? ReadImageWin32()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var dibHere = Native.IsClipboardFormatAvailable(CfDib);
        var bitmapHere = Native.IsClipboardFormatAvailable(CfBitmap);

        if (!dibHere && !bitmapHere) return null;
        if (!Native.OpenClipboard(IntPtr.Zero)) return null;

        try
        {
            if (dibHere)
            {
                var handle = Native.GetClipboardData(CfDib);
                if (handle == IntPtr.Zero) return null;

                var ptr = Native.GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;

                try
                {
                    var size = Native.GlobalSize(handle);
                    var dib = new byte[size];
                    Marshal.Copy(ptr, dib, 0, (int)size);

                    return DibToRgba(dib);
                }
                finally
                {
                    Native.GlobalUnlock(handle);
                }
            }

            var bitmap = Native.GetClipboardData(CfBitmap);
            if (bitmap == IntPtr.Zero) return null;

            var fromHandle = HBitmapToDib(bitmap);
            return fromHandle is null ? null : DibToRgba(fromHandle);
        }
        catch (Exception ex)
        {
            log.Warn($"could not read the clipboard image: {ex.Message}");
            return null;
        }
        finally
        {
            Native.CloseClipboard();
        }
    }

    /// <summary>HBITMAP به DIB با ۳۲ بیت — خروجی همان چیزی است که از مسیر CF_DIB می‌آید.</summary>
    private static byte[]? HBitmapToDib(IntPtr bitmap)
    {
        var dc = Native.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;

        try
        {
            var header = new byte[40];
            BitConverter.GetBytes(40).CopyTo(header, 0);
            BitConverter.GetBytes((short)1).CopyTo(header, 12);    // biPlanes
            BitConverter.GetBytes((short)32).CopyTo(header, 14);   // biBitCount
            BitConverter.GetBytes((int)0).CopyTo(header, 16);      // BI_RGB

            // پاس اول: بیرون کشیدن ابعاد بدون بافر پیکسل
            if (Native.GetDIBits(dc, bitmap, 0, 0, null, header, 0) == 0) return null;

            var width = BitConverter.ToInt32(header, 4);
            var height = BitConverter.ToInt32(header, 8);
            if (width <= 0 || height <= 0) return null;

            var pixels = new byte[width * height * 4];
            if (Native.GetDIBits(dc, bitmap, 0, (uint)height, pixels, header, 0) == 0) return null;

            var dib = new byte[40 + pixels.Length];
            header.CopyTo(dib, 0);
            pixels.CopyTo(dib, 40);

            return dib;
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    /// <summary>
    /// DIB بی‌فشرده با ۲۴ یا ۳۲ بیت به RGBA؛ ردیف‌ها در DIB از پایین به بالایند. فشرده‌ی BI_RGB
    /// عادی است و BI_BITFIELDS هم می‌آید — کلیپ‌بورد ویندوز موقع سنتز DIB از یک HBITMAP معمولا همان
    /// BITFIELDS با ماسک‌های استاندارد می‌سازد، پس نادیده گرفتنش یعنی ندیدن نصف تصویرهای دنیا.
    /// </summary>
    private static Contracts.ClipboardImage? DibToRgba(byte[] dib)
    {
        if (dib.Length < 40) return null;

        var headerSize = BitConverter.ToInt32(dib, 0);
        var width = BitConverter.ToInt32(dib, 4);
        var rawHeight = BitConverter.ToInt32(dib, 8);
        var bits = BitConverter.ToInt16(dib, 14);
        var compression = BitConverter.ToInt32(dib, 16);

        if (width <= 0 || rawHeight == 0 || (bits != 24 && bits != 32)) return null;

        var topDown = rawHeight < 0;
        var height = Math.Abs(rawHeight);
        var bytesPerPixel = bits / 8;
        var stride = (width * bytesPerPixel + 3) & ~3;

        // در BI_RGB کانال‌ها همیشه BGR درِ هم؛ فاصله‌ی پالت نداریم چون عمق ۲۴+
        var dataOffset = 40;
        int rShift = 16, gShift = 8, bShift = 0;

        if (compression == 3)
        {
            if (bits != 32 || dib.Length < 52) return null;

            rShift = MaskShift(BitConverter.ToUInt32(dib, 40));
            gShift = MaskShift(BitConverter.ToUInt32(dib, 44));
            bShift = MaskShift(BitConverter.ToUInt32(dib, 48));

            if (rShift < 0 || gShift < 0 || bShift < 0) return null;

            // سه DWORD ماسک بعد از سرصفحه؛ در BITMAPV4/V5 ماسک‌ها داخل خودِ سرصفحه‌ی بزرگ‌ترند
            dataOffset = headerSize == 40 ? 52 : headerSize;
        }
        else if (compression != 0)
        {
            return null;
        }

        if (dataOffset + stride * height > dib.Length) return null;

        var pixels = new byte[width * height * 4];
        var bitfields = compression == 3;

        for (var y = 0; y < height; y++)
        {
            var source = dataOffset + y * stride;
            var target = (topDown ? y : height - 1 - y) * width * 4;

            for (var x = 0; x < width; x++)
            {
                var s = source + x * bytesPerPixel;

                if (bitfields)
                {
                    var value = BitConverter.ToUInt32(dib, s);
                    pixels[target + x * 4 + 0] = (byte)((value >> rShift) & 0xFF);
                    pixels[target + x * 4 + 1] = (byte)((value >> gShift) & 0xFF);
                    pixels[target + x * 4 + 2] = (byte)((value >> bShift) & 0xFF);
                }
                else
                {
                    pixels[target + x * 4 + 0] = dib[s + 2];               // R از BGR
                    pixels[target + x * 4 + 1] = dib[s + 1];
                    pixels[target + x * 4 + 2] = dib[s + 0];
                }

                pixels[target + x * 4 + 3] = byte.MaxValue;
            }
        }

        return new Contracts.ClipboardImage { Pixels = pixels, Width = width, Height = height };
    }

    /// <summary>جای بایتِ یک کانال از ماسکش — 0x00FF0000 یعنی بایتِ دوم. ماسک ناشناخته، -۱.</summary>
    private static int MaskShift(uint mask) => mask switch
    {
        0xFF000000 => 24,
        0x00FF0000 => 16,
        0x0000FF00 => 8,
        0x000000FF => 0,
        _ => -1
    };

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint format);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalSize(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
                                           byte[]? bits, byte[] info, uint usage);
    }
}
