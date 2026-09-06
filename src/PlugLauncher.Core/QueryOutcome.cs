namespace PlugLauncher.Core;

/// <summary>نتیجه‌ی یک جستجو در هسته.</summary>
/// <param name="Items">ردیف‌های نهایی، مرتب‌شده.</param>
/// <param name="CorrectedQuery">
/// اگر متن کاربر با چیدمان اشتباه کیبورد تایپ شده بود و نتایج از روی خوانش اصلاح‌شده به دست آمده،
/// همان متن اصلاح‌شده؛ وگرنه null. UI با این مقدار به کاربر نشان می‌دهد چه چیزی واقعاً جستجو شده است.
/// </param>
public sealed record QueryOutcome(IReadOnlyList<SearchItem> Items, string? CorrectedQuery)
{
    public static readonly QueryOutcome Empty = new([], null);
}
