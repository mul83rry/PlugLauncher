// Text typed with the wrong keyboard layout. Keyword: kb
//
//   kb sghl          -> سلام        (typed on the Persian keys, delivered as US letters)
//   kb ff;h,          -> whatever your layouts make of it
//   kb               -> converts whatever is on the clipboard
//
// Nothing here is hardcoded to a language. The candidate conversions come from the keyboard
// layouts actually installed on this machine, asked of Windows directly: GetKeyboardLayoutList
// for the list, VkKeyScanEx to find which physical key produces a character on one layout, and
// ToUnicodeEx to ask what that same key produces on another. Install a third layout and this
// plugin starts offering it with no change here.
//
// Enter copies the row.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

static class Native
{
    // Passing 0/null asks only for the count, so the array is never guessed at.
    [DllImport("user32.dll")]
    public static extern int GetKeyboardLayoutList(int nBuff, [In, Out] IntPtr[]? lpList);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    // CharSet.Unicode on both of these is not optional. The default is Ansi, which marshals the
    // char argument through the system code page — so a Persian letter arrives as '?' — and reads
    // ToUnicodeEx's UTF-16 buffer one byte at a time, turning "سلام" into "3D'E", its low bytes.
    /// <summary>Low byte is the virtual key, high byte the shift state. -1 when the layout cannot type it.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
}

const uint MapVkToVsc = 0;
const int VkShift = 0x10;

// Windows 10 1607 and later: translate without touching the real keyboard state, so a dead key
// met here cannot leak into what the user types next.
const uint NoKeyStateChange = 0x4;

void Copy(string value)
{
    for (var attempt = 0; attempt < 3; attempt++)
    {
        try { System.Windows.Clipboard.SetText(value); return; }
        catch { Thread.Sleep(40); }
    }
}

string ClipboardText()
{
    try { return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty; }
    catch { return string.Empty; }
}

List<IntPtr> InstalledLayouts()
{
    var count = Native.GetKeyboardLayoutList(0, null);
    if (count <= 0) return [];

    var buffer = new IntPtr[count];
    Native.GetKeyboardLayoutList(count, buffer);

    return buffer.Where(h => h != IntPtr.Zero).Distinct().ToList();
}

string LayoutName(IntPtr hkl)
{
    // The low word of an HKL is the language identifier; the high word is the physical layout,
    // which is what makes two layouts of the same language distinct but is not worth naming.
    var langId = (int)((long)hkl & 0xFFFF);

    try { return CultureInfo.GetCultureInfo(langId).EnglishName; }
    catch { return $"layout 0x{langId:X4}"; }
}

/// <summary>Only letters and punctuation move; spaces and digits sit on the same keys everywhere.</summary>
bool IsTranslatable(char c) => !char.IsWhiteSpace(c) && !char.IsDigit(c);

/// <summary>The character that the key producing <paramref name="c"/> on <paramref name="from"/> gives on <paramref name="to"/>.</summary>
string? Retype(char c, IntPtr from, IntPtr to)
{
    var scan = Native.VkKeyScanEx(c, from);
    if (scan == -1) return null;

    var virtualKey = (uint)(scan & 0xFF);
    var shiftState = (scan >> 8) & 0xFF;

    // Ctrl or Alt in the shift state means this is an AltGr style combination, not a letter
    // somebody typed by mistake.
    if ((shiftState & 0x6) != 0) return null;

    var keyState = new byte[256];
    if ((shiftState & 0x1) != 0) keyState[VkShift] = 0x80;

    var scanCode = Native.MapVirtualKeyEx(virtualKey, MapVkToVsc, to);
    var buffer = new StringBuilder(8);

    var written = Native.ToUnicodeEx(virtualKey, scanCode, keyState, buffer, buffer.Capacity, NoKeyStateChange, to);

    // Negative means a dead key — it still tells us which character the key carries.
    var length = Math.Abs(written);
    if (length == 0) return null;

    return buffer.ToString(0, Math.Min(length, buffer.Length));
}

/// <summary>
/// Reads <paramref name="text"/> as if it had been typed on <paramref name="from"/> while
/// <paramref name="to"/> was really active. Null when the pair cannot explain the text.
/// </summary>
string? Reinterpret(string text, IntPtr from, IntPtr to)
{
    var builder = new StringBuilder(text.Length);
    var changed = false;

    foreach (var c in text)
    {
        if (!IsTranslatable(c))
        {
            builder.Append(c);
            continue;
        }

        var retyped = Retype(c, from, to);

        // A character this layout cannot produce at all means the user was not typing on it,
        // so the whole candidate is wrong rather than partly right.
        if (retyped is null) return null;

        if (retyped != c.ToString()) changed = true;
        builder.Append(retyped);
    }

    return changed ? builder.ToString() : null;
}

PluginResult Note(string title, string subtitle) => new()
{
    Id = "note",
    Title = title,
    Subtitle = subtitle,
    Score = 100
};

return Plugin.Create(query =>
{
    var text = query.Search;
    var fromClipboard = false;

    if (string.IsNullOrWhiteSpace(text))
    {
        text = ClipboardText();
        fromClipboard = true;
    }

    if (string.IsNullOrWhiteSpace(text))
        return [Note("Type the text that came out wrong", "kb sghl — or copy the text first and just type kb")];

    text = text.Trim();

    var layouts = InstalledLayouts();
    if (layouts.Count < 2)
        return [Note("Only one keyboard layout is installed",
                     $"{(layouts.Count == 1 ? LayoutName(layouts[0]) : "none")} — add a second one in Windows language settings")];

    var active = Native.GetKeyboardLayout(0);
    var rows = new List<PluginResult>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var from in layouts)
    {
        foreach (var to in layouts)
        {
            if (from == to) continue;

            var converted = Reinterpret(text, from, to);
            if (converted is null || !seen.Add(converted)) continue;

            // The common mistake is typing while the layout that is active right now was not the
            // one intended, so candidates that read the text as coming from it go first.
            var score = 100 + (from == active ? 30 : 0);

            rows.Add(new PluginResult
            {
                Id = $"{(long)from:X}->{(long)to:X}",
                Title = converted,
                Subtitle = $"{LayoutName(from)} → {LayoutName(to)}   ·   Enter copies",
                Score = score,
                Action = () => Copy(converted)
            });
        }
    }

    if (rows.Count == 0)
        return [Note("No layout explains that text",
                     $"Tried {layouts.Count} installed layouts: {string.Join(", ", layouts.Select(LayoutName).Distinct())}")];

    if (fromClipboard)
        rows.Insert(0, Note($"Reading the clipboard: {(text.Length > 40 ? text[..40] + "…" : text)}",
                            "Type the text after kb to convert something else"));

    return rows;
});
