namespace PlugLauncher.Contracts;

/// <summary>
/// پرسیدن یک سؤال بله/خیر از کاربر، قبل از کاری که برگشت ندارد.
///
/// مثل <see cref="Clipboard"/> میزبان پیاده‌سازی را می‌گذارد. پلاگین نباید بداند دیالوگ چطور
/// ساخته می‌شود — تا دیروز پلاگین <c>system</c> مستقیم <c>System.Windows.MessageBox</c> صدا
/// می‌زد و همان یک خط، پلاگین را به ویندوز و به یک کتابخانه‌ی UI مشخص می‌بست.
/// </summary>
public static class Ask
{
    private static Func<string, string, bool>? _confirm;

    /// <summary>میزبان این را یک‌بار موقع بالا آمدن صدا می‌زند.</summary>
    public static void Use(Func<string, string, bool> confirm) => _confirm = confirm;

    /// <summary>
    /// در نبود میزبان جواب «خیر» است، نه «بله»: پلاگینی که بدون پرسیدن کار خطرناکش را انجام
    /// بدهد بدتر از پلاگینی است که هیچ کاری نکند.
    /// </summary>
    public static bool Confirm(string title, string question)
        => _confirm?.Invoke(title ?? string.Empty, question ?? string.Empty) ?? false;
}
