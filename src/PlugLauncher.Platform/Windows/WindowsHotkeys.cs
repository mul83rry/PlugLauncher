using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.Windows;

/// <summary>
/// هات‌کی سراسری ویندوز با <c>RegisterHotKey</c>.
///
/// پنجره‌ی خودش را می‌سازد — یک پنجره‌ی «فقط پیام» که هرگز دیده نمی‌شود — به‌جای اینکه هندلِ
/// پنجره‌ی اصلی را بگیرد. دلیلش این است که هندلِ پنجره‌ی اصلی از UI می‌آید، و اگر این کلاس به آن
/// وابسته باشد، لایه‌ی پلتفرم دوباره به UI گره می‌خورد؛ همان چیزی که قرار است اینجا تمام شود.
///
/// ویندوز پیام <c>WM_HOTKEY</c> را به صف پیام همان تردی می‌فرستد که پنجره را ساخته، پس ساختن
/// نمونه باید روی ترد UI باشد. حلقه‌ی پیامِ خودِ برنامه آن را تحویل می‌دهد.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkeys : IHotkeys
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xB0B0;
    private const int HwndMessage = -3;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint NoRepeat = 0x4000;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public int style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static readonly string ClassName = "PlugLauncherHotkeyWindow";
    private static bool _classRegistered;

    // نگه داشتن دلیگیت لازم است: ویندوز فقط اشاره‌گر خام دارد و GC خبر ندارد
    private readonly WndProc _handler;

    private IntPtr _window;
    private Action? _pressed;
    private bool _registered;

    public WindowsHotkeys()
    {
        _handler = HandleMessage;
        _window = CreateMessageWindow();
    }

    public bool TryRegister(Hotkey hotkey, Action pressed, out string problem)
    {
        Unregister();

        if (_window == IntPtr.Zero)
        {
            problem = "the launcher could not create the window that receives the hotkey";
            return false;
        }

        if (!WindowsKeys.TryVirtualKey(hotkey.Key, out var virtualKey))
        {
            problem = $"\"{hotkey.Key}\" is not a key this can register";
            return false;
        }

        var modifiers = NoRepeat;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= 1;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= 2;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= 4;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Meta)) modifiers |= 8;

        _pressed = pressed;
        _registered = RegisterHotKey(_window, HotkeyId, modifiers, virtualKey);

        if (_registered)
        {
            problem = "";
            return true;
        }

        // شایع‌ترین دلیل: برنامه‌ی دیگری همین ترکیب را گرفته است
        _pressed = null;
        problem = $"another application already has \"{hotkey}\"";
        return false;
    }

    public void Unregister()
    {
        if (!_registered) return;

        UnregisterHotKey(_window, HotkeyId);
        _registered = false;
        _pressed = null;
    }

    private IntPtr CreateMessageWindow()
    {
        if (!_classRegistered)
        {
            var windowClass = new WndClassEx
            {
                cbSize = Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = _handler,
                lpszClassName = ClassName
            };

            // 1410 یعنی کلاس از قبل هست؛ در یک پروسه فقط یک‌بار ثبت می‌شود و همان کافی است
            if (RegisterClassEx(ref windowClass) == 0 &&
                Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
            {
                return IntPtr.Zero;
            }

            _classRegistered = true;
        }

        return CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr HandleMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _pressed?.Invoke();
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Unregister();

        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
    }
}
