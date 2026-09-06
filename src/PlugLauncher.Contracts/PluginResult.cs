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
