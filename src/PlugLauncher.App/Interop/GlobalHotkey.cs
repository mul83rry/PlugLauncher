using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PlugLauncher.App.Interop;

/// <summary>ثبت یک هات‌کی سراسری ویندوز و تبدیل آن به یک رویداد ساده.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xB0B0;

    [Flags]
    private enum Modifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8,
        NoRepeat = 0x4000
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private bool _registered;

    /// <summary>وقتی کاربر هات‌کی را می‌زند.</summary>
    public event EventHandler? Pressed;

    /// <summary>ثبت هات‌کی روی پنجره‌ی داده‌شده. مثال ورودی: <c>Alt+Space</c>.</summary>
    public bool Register(Window window, string hotkey)
    {
        Unregister();

        if (!TryParse(hotkey, out var modifiers, out var key)) return false;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(HandleMessage);

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(hwnd, HotkeyId, (uint)(modifiers | Modifiers.NoRepeat), virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_source is null) return;

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(HandleMessage);
        _source = null;
    }

    private IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>تبدیل رشته‌ی هات‌کی («Alt+Shift+Space») به مادیفایر و کلید.</summary>
    private static bool TryParse(string hotkey, out Modifiers modifiers, out Key key)
    {
        modifiers = Modifiers.None;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        foreach (var raw in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "alt":
                    modifiers |= Modifiers.Alt;
                    break;
                case "ctrl":
                case "control":
                    modifiers |= Modifiers.Control;
                    break;
                case "shift":
                    modifiers |= Modifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= Modifiers.Win;
                    break;
                default:
                    if (!Enum.TryParse(raw, ignoreCase: true, out Key parsed)) return false;
                    key = parsed;
                    break;
            }
        }

        return key != Key.None;
    }

    public void Dispose() => Unregister();
}
