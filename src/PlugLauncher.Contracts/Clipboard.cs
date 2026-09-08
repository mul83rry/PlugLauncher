namespace PlugLauncher.Contracts;

/// <summary>
/// کلیپ‌بورد سیستم، از طرف میزبان.
///
/// پلاگین‌ها تا اینجا مستقیم <c>System.Windows.Clipboard</c> را صدا می‌زدند و همان یک خط،
/// پلاگینی مثل ماشین‌حساب را که هیچ چیز ویندوزی در منطقش نیست، به WPF گره می‌زد. یعنی به ویندوز.
/// حالا میزبان پیاده‌سازی را می‌گذارد و پلاگین نمی‌داند UI با چه چیزی نوشته شده.
///
/// استاتیک است چون کلیپ‌بورد هم استاتیک است: یکی بیشتر در کل سیستم نیست، و پلاگینی که فقط
/// می‌خواهد یک رشته را کپی کند نباید مجبور شود <see cref="IPluginContext"/> بگیرد.
/// </summary>
public static class Clipboard
{
    private static Action<string>? _copy;
    private static Func<string>? _read;

    /// <summary>میزبان یک‌بار موقع بالا آمدن، قبل از لود پلاگین‌ها، صدا می‌زند.</summary>
    public static void Use(Action<string> copy, Func<string> read)
    {
        _copy = copy;
        _read = read;
    }

    /// <summary>گذاشتن متن روی کلیپ‌بورد. جایی که میزبان چیزی نگذاشته باشد، بی‌صدا کاری نمی‌کند.</summary>
    public static void Copy(string text) => _copy?.Invoke(text ?? string.Empty);

    /// <summary>متن روی کلیپ‌بورد، یا رشته‌ی خالی اگر چیزی نباشد.</summary>
    public static string Text() => _read?.Invoke() ?? string.Empty;
}
