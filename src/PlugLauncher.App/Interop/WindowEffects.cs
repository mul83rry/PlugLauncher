using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PlugLauncher.App.Interop;

/// <summary>
/// افکت شیشه‌ای (Acrylic) و گوشه‌های گرد ویندوز ۱۱ روی پنجره‌ی WPF.
/// روی ویندوزهای قدیمی‌تر بی‌صدا رد می‌شود و پنجره با رنگ ساده‌ی خودش نمایش داده می‌شود.
/// </summary>
public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int CornerPreferenceRound = 2;
    private const int BackdropTypeTransientWindow = 3; // Acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    /// <summary>باید بعد از ساخته شدن هندل پنجره (SourceInitialized) صدا زده شود.</summary>
    public static bool TryApplyAcrylic(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        try
        {
            // پس‌زمینه باید شفاف باشد تا backdrop سیستم دیده شود
            var source = HwndSource.FromHwnd(hwnd);
            if (source is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var corner = CornerPreferenceRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var backdrop = BackdropTypeTransientWindow;
            return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
