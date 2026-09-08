using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.MacOs;

/// <summary>
/// هات‌کی سراسری مک با <c>RegisterEventHotKey</c> از کربن.
///
/// کربن قدیمی است ولی این تنها راهِ عمومی و بدون‌اجازه است. راه دیگر، <c>CGEventTap</c>، کل
/// کلیدهای سیستم را می‌بیند و برای همین اجازه‌ی Accessibility می‌خواهد — یعنی کاربر باید در
/// تنظیمات امنیتی تیک بزند تا لانچر بالا بیاید. این یکی هیچ اجازه‌ای نمی‌خواهد، چون سیستم فقط
/// همان یک ترکیب را به ما می‌دهد و بقیه را نه.
///
/// دو تکه‌ی جدا لازم است: یک <c>EventHandler</c> که رویدادِ فشرده‌شدن را تحویل می‌گیرد، و ثبت خودِ
/// ترکیب. اولی یک‌بار نصب می‌شود و می‌ماند، دومی با هر تغییرِ هات‌کی عوض می‌شود.
///
/// کربن رویداد را به حلقه‌ی اصلی برنامه می‌دهد، پس ساختن نمونه و صدا زدن <see cref="TryRegister"/>
/// باید روی ترد UI باشد؛ <c>pressed</c> هم روی همان ترد صدا زده می‌شود.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacHotkeys : IHotkeys
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // 'keyb' و 'PLUG'، همان‌طور که کربن این چهار حرفی‌ها را به عدد می‌نویسد
    private const uint EventClassKeyboard = 0x6B657962;
    private const uint Signature = 0x504C5547;

    private const uint EventHotKeyPressed = 5;
    private const int HotKeyExistsErr = -9878;

    // مادیفایرهای کربن، از Events.h
    private const uint CmdKey = 0x0100;
    private const uint ShiftKey = 0x0200;
    private const uint OptionKey = 0x0800;
    private const uint ControlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint Class;
        public uint Kind;
    }

    private delegate int EventHandler(IntPtr callRef, IntPtr theEvent, IntPtr userData);

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(
        IntPtr target, EventHandler handler, nuint count,
        [In] EventTypeSpec[] types, IntPtr userData, out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(
        uint keyCode, uint modifiers, EventHotKeyId id,
        IntPtr target, uint options, out IntPtr hotkeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotkeyRef);

    // نگه داشتن دلیگیت لازم است: کربن فقط یک اشاره‌گر خام دارد و GC از آن خبر ندارد
    private readonly EventHandler _handler;

    private IntPtr _handlerRef;
    private IntPtr _hotkeyRef;
    private Action? _pressed;

    public MacHotkeys() => _handler = HandlePressed;

    public bool TryRegister(Hotkey hotkey, Action pressed, out string problem)
    {
        Unregister();

        // اگر نامی از این چهار تابع در کربنِ این نسخه‌ی مک نباشد، فراخوانی استثنا پرت می‌کند.
        // این کلاس روی مک واقعی تست نشده، پس همان استثنا هم باید مثل هر شکست دیگری به یک
        // جمله تبدیل شود؛ لانچری که بالا نمی‌آید باید بگوید چرا، نه اینکه کرش کند.
        try
        {
            return Register(hotkey, pressed, out problem);
        }
        catch (Exception ex)
        {
            _pressed = null;
            problem = ex.Message;
            return false;
        }
    }

    private bool Register(Hotkey hotkey, Action pressed, out string problem)
    {
        if (!MacKeys.TryKeyCode(hotkey.Key, out var keyCode))
        {
            problem = $"\"{hotkey.Key}\" is not a key this can register on macOS";
            return false;
        }

        if (!InstallHandler(out problem)) return false;

        // Alt همان Option است و Meta همان Command — یک کلید با دو اسم، مثل بقیه‌ی جاها
        var modifiers = 0u;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= OptionKey;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= ControlKey;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= ShiftKey;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Meta)) modifiers |= CmdKey;

        var id = new EventHotKeyId { Signature = Signature, Id = 1 };

        _pressed = pressed;
        var status = RegisterEventHotKey(keyCode, modifiers, id, GetApplicationEventTarget(), 0, out _hotkeyRef);

        if (status == 0 && _hotkeyRef != IntPtr.Zero)
        {
            problem = "";
            return true;
        }

        _pressed = null;
        _hotkeyRef = IntPtr.Zero;

        problem = status == HotKeyExistsErr
            ? $"another application already has \"{hotkey}\""
            : $"macOS turned down \"{hotkey}\" (error {status})";

        return false;
    }

    /// <summary>
    /// یک‌بار برای همه‌ی عمر برنامه. ثبت ترکیب و شنیدنِ فشرده‌شدن در کربن دو کار جدا هستند و
    /// نگه داشتن این یکی یعنی عوض کردن هات‌کی از تنظیمات، هربار یک هندلر تازه روی هم نمی‌چیند.
    /// </summary>
    private bool InstallHandler(out string problem)
    {
        problem = "";
        if (_handlerRef != IntPtr.Zero) return true;

        EventTypeSpec[] types = [new EventTypeSpec { Class = EventClassKeyboard, Kind = EventHotKeyPressed }];

        var status = InstallEventHandler(
            GetApplicationEventTarget(), _handler, 1, types, IntPtr.Zero, out _handlerRef);

        if (status == 0 && _handlerRef != IntPtr.Zero) return true;

        _handlerRef = IntPtr.Zero;
        problem = $"macOS would not let the launcher listen for hotkeys (error {status})";
        return false;
    }

    private int HandlePressed(IntPtr callRef, IntPtr theEvent, IntPtr userData)
    {
        // فقط یک ترکیب ثبت می‌شود، پس بیرون کشیدن شناسه از دل رویداد چیزی روشن نمی‌کند
        _pressed?.Invoke();
        return 0;
    }

    public void Unregister()
    {
        _pressed = null;
        if (_hotkeyRef == IntPtr.Zero) return;

        try { UnregisterEventHotKey(_hotkeyRef); } catch { /* در حال بسته شدن؛ گزارشش به کسی نمی‌رسد */ }
        _hotkeyRef = IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();

        if (_handlerRef == IntPtr.Zero) return;

        try { RemoveEventHandler(_handlerRef); } catch { /* همان بالا */ }
        _handlerRef = IntPtr.Zero;
    }
}
