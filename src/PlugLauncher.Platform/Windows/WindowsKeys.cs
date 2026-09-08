namespace PlugLauncher.Platform.Windows;

/// <summary>
/// نام کلید به کد مجازی ویندوز.
///
/// نام‌های WPF («Space»، «D1»، «Oem3») هم پذیرفته می‌شوند، چون هات‌کی‌های ذخیره‌شده‌ی قبلی با
/// همان املا نوشته شده‌اند و کاربر نباید تنظیماتش را از دست بدهد.
/// </summary>
internal static class WindowsKeys
{
    public static bool TryVirtualKey(string name, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;

        var key = name.Trim();

        // A تا Z و 0 تا 9 همان کد اسکی خودشان را دارند
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        // D1 تا D0: املای WPF برای ردیف اعداد
        if (key.Length == 2 && char.ToUpperInvariant(key[0]) == 'D' && char.IsAsciiDigit(key[1]))
        {
            virtualKey = key[1];
            return true;
        }

        if (key.Length is 2 or 3 && char.ToUpperInvariant(key[0]) == 'F'
            && int.TryParse(key[1..], out var number) && number is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x6F + number);
            return true;
        }

        virtualKey = key.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "back" or "backspace" => 0x08,
            "insert" => 0x2D,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "prior" => 0x21,
            "pagedown" or "next" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "capslock" => 0x14,
            "numlock" => 0x90,
            "printscreen" or "snapshot" => 0x2C,
            "pause" => 0x13,

            // کلیدهای علامت‌دار: هم با اسم WPF، هم با خود علامت
            "oem3" or "`" or "backquote" or "tilde" => 0xC0,
            "oemminus" or "-" or "minus" => 0xBD,
            "oemplus" or "=" or "equals" => 0xBB,
            "oem4" or "[" => 0xDB,
            "oem6" or "]" => 0xDD,
            "oem5" or "\\" => 0xDC,
            "oem1" or ";" => 0xBA,
            "oem7" or "'" => 0xDE,
            "oemcomma" or "," => 0xBC,
            "oemperiod" or "." => 0xBE,
            "oem2" or "/" => 0xBF,

            _ => 0
        };

        return virtualKey != 0;
    }
}
