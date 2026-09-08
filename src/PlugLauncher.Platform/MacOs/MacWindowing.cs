using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.MacOs;

/// <summary>
/// همان دو کار کوچکِ ویندوزی، این‌بار روی مک.
///
/// هر دو شکستشان بی‌صداست: اینجا هرکدام یک بهبود است نه یک قابلیت، و اگر جواب ندهند پوسته
/// سراغ راه پیش‌فرض خودش می‌رود.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindowing : IWindowing
{
    private const string ObjC = "/usr/lib/libobjc.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(ObjC, EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetSelector(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendBool(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [StructLayout(LayoutKind.Sequential)]
    private struct CgPoint
    {
        public double X, Y;
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CgPoint CGEventGetLocation(IntPtr handle);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr handle);

    /// <summary>
    /// روی مک پنجره جلو نمی‌آید، <b>برنامه</b> جلو می‌آید — به همین دلیل هندلِ پنجره اینجا به کار
    /// نمی‌آید و کنار گذاشته شده. بدون این، پنجره دیده می‌شود ولی کیبورد هنوز دست برنامه‌ی قبلی
    /// است و لانچری که نمی‌شود در آن تایپ کرد به درد نمی‌خورد.
    /// </summary>
    public bool TryFocus(nint window)
    {
        try
        {
            var app = Send(GetClass("NSApplication"), GetSelector("sharedApplication"));
            if (app == IntPtr.Zero) return false;

            SendBool(app, GetSelector("activateIgnoringOtherApps:"), true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// مختصات اینجا «پوینت» است نه پیکسل، ولی صفحه‌های آوالونیا روی مک هم با همان واحد شمرده
    /// می‌شوند، پس چیزی برای تبدیل نمی‌ماند.
    /// </summary>
    public bool TryCursor(out int x, out int y)
    {
        x = y = 0;

        try
        {
            // موس جای خودش را در هیچ متغیر سراسری‌ای نمی‌گذارد؛ راهش ساختن یک رویداد خالی و
            // پرسیدنِ جای آن است
            var handle = CGEventCreate(IntPtr.Zero);
            if (handle == IntPtr.Zero) return false;

            try
            {
                var point = CGEventGetLocation(handle);
                x = (int)point.X;
                y = (int)point.Y;
                return true;
            }
            finally
            {
                CFRelease(handle);
            }
        }
        catch
        {
            return false;
        }
    }
}
