using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PlugLauncher.App.Interop;

/// <summary>
/// گرفتن قطعی فوکوس برای پنجره‌ی لانچر. ویندوز اجازه‌ی <c>SetForegroundWindow</c> را به پروسه‌ای
/// که فورگراند نیست محدود می‌کند؛ راه استاندارد این است که موقتاً ورودی نخ پنجره‌ی فعلی به نخ ما
/// وصل شود تا ویندوز ما را «همان کاربر فعال» بداند.
/// </summary>
public static class ForegroundHelper
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

    public static void ForceForeground(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var foreground = GetForegroundWindow();
        if (foreground == hwnd) return;

        var thisThread = GetCurrentThreadId();
        var otherThread = foreground == IntPtr.Zero ? thisThread : GetWindowThreadProcessId(foreground, out _);

        var attached = otherThread != thisThread && AttachThreadInput(otherThread, thisThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(otherThread, thisThread, false);
        }
    }
}
