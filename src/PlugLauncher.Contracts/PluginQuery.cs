namespace PlugLauncher.Contracts;

/// <summary>متن جستجویی که کاربر تایپ کرده، به شکل تفکیک‌شده.</summary>
/// <param name="Raw">کل متن باکس جستجو، بدون تغییر.</param>
/// <param name="Keyword">کلیدواژه‌ی پلاگین اگر کاربر آن را تایپ کرده باشد (مثلاً <c>st</c>)، وگرنه خالی.</param>
/// <param name="Search">متن جستجو بعد از حذف کلیدواژه — چیزی که پلاگین باید رویش سرچ کند.</param>
public readonly record struct PluginQuery(string Raw, string Keyword, string Search)
{
    /// <summary>کلمات جستجو، جدا شده با فاصله.</summary>
    public string[] Terms => Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>وقتی کاربر هنوز چیزی تایپ نکرده است.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Search);

    /// <summary>وقتی کاربر صراحتاً کلیدواژه‌ی این پلاگین را تایپ کرده است.</summary>
    public bool HasKeyword => !string.IsNullOrEmpty(Keyword);

    public override string ToString() => Raw;
}
