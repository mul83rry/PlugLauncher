namespace PlugLauncher.Contracts;

/// <summary>یک ردیف از لیست نتایج.</summary>
public sealed class PluginResult
{
    /// <summary>خط اول ردیف.</summary>
    public required string Title { get; init; }

    /// <summary>خط دوم ردیف (مثلاً مسیر فایل).</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// مسیر آیکن؛ نسبی نسبت به پوشه‌ی پلاگین (مثلاً <c>assets/icon.png</c>) یا مسیر مطلق.
    /// اگر خالی باشد، آیکن خود پلاگین استفاده می‌شود.
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>امتیاز مرتب‌سازی؛ هرچه بیشتر، بالاتر در لیست.</summary>
    public int Score { get; init; }

    /// <summary>
    /// سهمی از یک کل، بین ۰ و ۱، که لانچر به شکل نواری کم‌رنگ پشت ردیف می‌کشد — حجم یک پوشه از
    /// درایو، درصد باتری، پیشرفت یک دانلود. <c>null</c> یعنی ردیف نوار ندارد. مقدار خارج از
    /// بازه بریده می‌شود، نه اینکه رد شود.
    /// </summary>
    public double? Fraction { get; init; }

    /// <summary>
    /// اگر مقدار داشته باشد، لانچر همین کوئری را بعد از این تعداد میلی‌ثانیه دوباره از پلاگین
    /// می‌پرسد — برای کاری که در پس‌زمینه ادامه دارد و ردیف فعلی فقط گزارش پیشرفتش است.
    /// وقتی کار تمام شد پلاگین دیگر این را نمی‌گذارد و تازه‌سازی خودبه‌خود می‌ایستد.
    ///
    /// میزبان سه سقف روی این می‌گذارد تا یک پلاگین معیوب نتواند حلقه‌ی بی‌پایان بسازد: فاصله
    /// کمتر از ۲۵۰ms نمی‌شود، برای هر متنِ تایپ‌شده دو دقیقه بودجه هست، و با بسته شدن پنجره
    /// همه‌چیز می‌ایستد.
    /// </summary>
    public int? RefreshAfterMs { get; init; }

    /// <summary>
    /// کاری که با Enter/کلیک انجام می‌شود. مقدار برگشتی مشخص می‌کند پنجره بسته شود یا نه
    /// (<c>true</c> = بسته شود).
    /// </summary>
    public Func<CancellationToken, Task<bool>>? ActionAsync { get; init; }

    /// <summary>نسخه‌ی ساده‌ی <see cref="ActionAsync"/> برای کارهای همگام؛ بعد از اجرا پنجره بسته می‌شود.</summary>
    public Action? Action
    {
        init
        {
            if (value is null) return;
            _syncAction = value;
        }
    }

    private Action? _syncAction;

    /// <summary>
    /// اگر مقدار داشته باشد، این ردیف کاری انجام نمی‌دهد؛ فقط متن باکس جستجو با این مقدار جایگزین
    /// می‌شود و پنجره باز می‌ماند. برای ردیف‌های راهنما/نمونه است تا کاربر بتواند یک گزینه را
    /// امتحان کند بدون آن‌که نحوش را حفظ کرده باشد.
    /// </summary>
    public string? ReplaceQuery { get; init; }

    /// <summary>
    /// متن بلندی که به‌جای اجرای <see cref="Action"/> در یک نما نمایش داده می‌شود: یک تکست‌باکس
    /// فقط‌خواندنی با دکمه‌ی کپی در گوشه‌ی راستش. برای جواب‌هایی است که در یک خط جا نمی‌شوند —
    /// بدنه‌ی یک پاسخ HTTP، خروجی یک دستور، متن یک فایل. <c>null</c> یعنی ردیف نما ندارد و همان
    /// رفتار همیشگی را دارد. نما با Esc بسته می‌شود و به لیست برمی‌گردد.
    /// </summary>
    public string? DetailText { get; init; }

    /// <summary>عنوان نما؛ اگر خالی باشد عنوان خود ردیف استفاده می‌شود.</summary>
    public string? DetailTitle { get; init; }

    /// <summary>داده‌ی دلخواه پلاگین که همراه ردیف حمل می‌شود.</summary>
    public object? Data { get; init; }

    /// <summary>شناسه‌ی پایدار ردیف برای ثبت آمار استفاده. اگر خالی باشد از عنوان استفاده می‌شود.</summary>
    public string? Id { get; init; }

    /// <summary>اجرای کار ردیف؛ هماهنگ‌کننده‌ی بین <see cref="Action"/> و <see cref="ActionAsync"/>.</summary>
    public async Task<bool> InvokeAsync(CancellationToken cancellationToken)
    {
        if (ActionAsync is not null) return await ActionAsync(cancellationToken).ConfigureAwait(false);
        if (_syncAction is not null)
        {
            _syncAction();
            return true;
        }

        return false;
    }
}
