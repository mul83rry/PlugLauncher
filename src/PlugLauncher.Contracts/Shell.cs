namespace PlugLauncher.Contracts;

/// <summary>
/// باز کردن یک فایل، پوشه یا آدرس با همان برنامه‌ای که سیستم برایش انتخاب می‌کند.
///
/// سومین چیزی که میزبان جایش می‌گذارد، بعد از <see cref="Clipboard"/> و <see cref="Ask"/>، و به
/// همان دلیل: پلاگین‌ها <c>explorer.exe</c> صدا می‌زدند. باز کردن یک پوشه ایده‌ی ویندوزی نیست،
/// ولی <c>explorer.exe /select,</c> هست — و همان یک خط، پلاگینی را که منطقش همه‌جا کار می‌کند
/// به ویندوز می‌بست.
/// </summary>
public static class Shell
{
    private static Action<string>? _open;
    private static Action<string>? _reveal;

    /// <summary>میزبان یک‌بار موقع بالا آمدن، قبل از لود پلاگین‌ها، صدا می‌زند.</summary>
    public static void Use(Action<string> open, Action<string> reveal)
    {
        _open = open;
        _reveal = reveal;
    }

    /// <summary>یک فایل، پوشه یا آدرس. جایی که میزبان چیزی نگذاشته باشد، بی‌صدا کاری نمی‌کند.</summary>
    public static void Open(string target)
    {
        if (!string.IsNullOrWhiteSpace(target)) _open?.Invoke(target);
    }

    /// <summary>
    /// نشان دادن یک فایل در فایل‌منیجر، انتخاب‌شده. اگر سیستم بلد نباشد، میزبان پوشه‌اش را باز
    /// می‌کند — چون «نشانم بده کجاست» جوابِ تقریبی هم دارد.
    /// </summary>
    public static void Reveal(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) _reveal?.Invoke(path);
    }
}
