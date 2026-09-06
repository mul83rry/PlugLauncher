using System.Text;

namespace PlugLauncher.Core;

/// <summary>
/// تبدیل متن تایپ‌شده با چیدمان اشتباه کیبورد. وقتی کاربر می‌خواهد <c>chrome</c> تایپ کند ولی زبان
/// ویندوز روی فارسی مانده، چیزی که در باکس می‌نشیند <c>زاقخپث</c> است؛ این کلاس همان متن را به حرفی
/// که روی همان کلید فیزیکی است برمی‌گرداند. عکسش هم پشتیبانی می‌شود (فارسی تایپ‌شده با کیبورد انگلیسی).
///
/// مبنا چیدمان استاندارد فارسی ویندوز (kbdfa) است.
/// </summary>
public static class KeyboardLayout
{
    // ردیف‌های اصلی کیبورد، حرف‌به‌حرف روی همان کلید فیزیکی
    private const string EnglishKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,";
    private const string PersianKeys = "ضصثقفغعهخحجچشسیبلاتنمکگظطزرذدپو";

    /// <summary>حرف‌هایی که با Shift روی چیدمان فارسی تولید می‌شوند (یعنی کاربر حرف بزرگ زده است).</summary>
    private static readonly Dictionary<char, char> ShiftedPersian = new()
    {
        ['ؤ'] = 'a', ['ئ'] = 's', ['ي'] = 'd', ['إ'] = 'f', ['أ'] = 'g',
        ['آ'] = 'h', ['ة'] = 'j', ['ژ'] = 'c', ['ء'] = 'm'
    };

    private static readonly Dictionary<char, char> PersianToEnglish = BuildPersianToEnglish();
    private static readonly Dictionary<char, char> EnglishToPersian = BuildEnglishToPersian();

    private static Dictionary<char, char> BuildPersianToEnglish()
    {
        var map = new Dictionary<char, char>();

        for (var i = 0; i < PersianKeys.Length; i++) map[PersianKeys[i]] = EnglishKeys[i];
        foreach (var (fa, en) in ShiftedPersian) map[fa] = en;

        // ارقام فارسی و عربی — روی چیدمان فارسی، ردیف عددی همین‌ها را می‌دهد
        for (var i = 0; i < 10; i++)
        {
            map[(char)('۰' + i)] = (char)('0' + i);
            map[(char)('٠' + i)] = (char)('0' + i);
        }

        return map;
    }

    private static Dictionary<char, char> BuildEnglishToPersian()
    {
        var map = new Dictionary<char, char>();

        for (var i = 0; i < EnglishKeys.Length; i++)
        {
            map[EnglishKeys[i]] = PersianKeys[i];
            map[char.ToUpperInvariant(EnglishKeys[i])] = PersianKeys[i];
        }

        // ارقام عمداً نگاشته نمی‌شوند: «24» در جستجو تقریباً همیشه یعنی همان عدد، نه «۲۴»
        return map;
    }

    /// <summary>هر چیزی از بلوک عربی/فارسی یونیکد.</summary>
    private static bool IsPersianScript(char c) => c is >= '\u0600' and <= '\u06FF';

    /// <summary>
    /// اگر متن با چیدمان اشتباه تایپ شده باشد، خوانشِ درستِ همان کلیدها را برمی‌گرداند.
    /// جهت تبدیل از روی خود متن حدس زده می‌شود: وجود حتی یک حرف فارسی یعنی «فارسی → انگلیسی».
    /// </summary>
    public static bool TryFix(string? text, out string fixedText)
    {
        fixedText = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var map = text.Any(IsPersianScript) ? PersianToEnglish : EnglishToPersian;
        var builder = new StringBuilder(text.Length);
        var changed = false;

        foreach (var c in text)
        {
            if (map.TryGetValue(c, out var mapped))
            {
                builder.Append(mapped);
                changed = true;
            }
            else
            {
                // فاصله، عدد و هر چیز نگاشته‌نشده دست‌نخورده می‌ماند
                builder.Append(c);
            }
        }

        if (!changed) return false;

        fixedText = builder.ToString();
        return !string.Equals(fixedText, text, StringComparison.Ordinal);
    }
}
