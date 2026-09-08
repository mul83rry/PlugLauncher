using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.Windows;

/// <summary>
/// دو کار کوچک که هیچ کتابخانه‌ی UI میان‌پلتفرمی انجامشان نمی‌دهد، ولی یک لانچرِ هات‌کی‌محور
/// بدونشان ناقص است: جلو آوردن قطعیِ پنجره، و اینکه موس روی کدام نمایشگر است.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWindowing : IWindowing
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    /// <summary>
    /// ویندوز اجازه‌ی <c>SetForegroundWindow</c> را به پروسه‌ای که فورگراند نیست محدود می‌کند —
    /// و پروسه‌ای که با هات‌کی بیدار شده دقیقاً همین وضع را دارد. راه استاندارد این است که ورودی
    /// نخِ پنجره‌ی فعلی موقتاً به نخ ما وصل شود تا ویندوز ما را «همان کاربر فعال» بداند.
    /// </summary>
    public bool TryFocus(nint window)
    {
        if (window == 0) return false;

        var foreground = GetForegroundWindow();
        if (foreground == window) return true;

        var thisThread = GetCurrentThreadId();
        var otherThread = foreground == IntPtr.Zero ? thisThread : GetWindowThreadProcessId(foreground, out _);

        var attached = otherThread != thisThread && AttachThreadInput(otherThread, thisThread, true);
        try
        {
            BringWindowToTop(window);
            return SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(otherThread, thisThread, false);
        }
    }

    public bool TryCursor(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = y = 0;
        return false;
    }
}
