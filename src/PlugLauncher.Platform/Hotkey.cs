namespace PlugLauncher.Platform;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,

    /// <summary>کلید ویندوز، Command روی مک، Super روی لینوکس — یک کلید با سه اسم.</summary>
    Meta = 8
}

/// <summary>
/// یک ترکیب کلید، مستقل از سیستم‌عامل: مادیفایرها به‌علاوه‌ی <b>نام</b> کلید، نه کد عددی‌اش.
///
/// عمداً نام و نه کد: ویندوز کد مجازی دارد، مک کد Carbon و لینوکس keysym، و هیچ‌کدام با هم یکی
/// نیستند. آنچه در <c>settings.json</c> می‌نشیند و چیزی که کاربر تایپ می‌کند همان «Alt+Space»
/// است؛ ترجمه‌اش به عدد، کارِ همان سیستم‌عاملی است که قرار است ثبتش کند.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public bool IsEmpty => string.IsNullOrEmpty(Key);

    /// <summary>خواندن رشته‌هایی مثل <c>Alt+Space</c> یا <c>Ctrl+Shift+K</c>.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        var key = "";

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt" or "option": modifiers |= HotkeyModifiers.Alt; break;
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win" or "windows" or "cmd" or "command" or "super" or "meta":
                    modifiers |= HotkeyModifiers.Meta;
                    break;

                default:
                    // دو کلید در یک ترکیب معنی ندارد و معمولاً یعنی کاربر اشتباه تایپ کرده
                    if (key.Length > 0) return false;
                    key = part;
                    break;
            }
        }

        if (key.Length == 0) return false;

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) parts.Add("Win");
        parts.Add(Key);

        return string.Join("+", parts);
    }
}
