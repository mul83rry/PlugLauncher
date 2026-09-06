namespace PlugLauncher.Store;

/// <summary>تنظیمات فروشگاه؛ از بخش <c>Store</c> در appsettings یا متغیرهای محیطی خوانده می‌شود.</summary>
public sealed class StoreOptions
{
    /// <summary>ریشه‌ی نگه‌داری بسته‌ها روی دیسک.</summary>
    public string DataRoot { get; set; } = "data";

    /// <summary>کلید انتشار (هدر <c>X-Api-Key</c>)؛ اگر خالی باشد انتشار کاملاً غیرفعال است.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>حداکثر حجم فایل بسته (بایت).</summary>
    public long MaxPackageBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>حداکثر مجموع حجم بازشده — محافظت در برابر zip bomb.</summary>
    public long MaxExtractedBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>حداکثر تعداد فایل داخل بسته.</summary>
    public int MaxEntries { get; set; } = 2000;
}
