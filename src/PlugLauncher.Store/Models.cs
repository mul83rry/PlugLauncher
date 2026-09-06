namespace PlugLauncher.Store;

/// <summary>یک بسته در لیست فروشگاه (همیشه آخرین نسخه).</summary>
public sealed record PluginSummary(
    string Id,
    string Name,
    string Description,
    string Version,
    string Author,
    IReadOnlyList<string> Keywords,
    long Size,
    string Sha256,
    DateTimeOffset PublishedAt,
    int Downloads,
    string DownloadUrl,
    string? IconUrl);

/// <summary>یک نسخه‌ی منتشرشده از یک بسته.</summary>
public sealed record PluginVersionInfo(
    string Version,
    long Size,
    string Sha256,
    DateTimeOffset PublishedAt,
    string DownloadUrl);

/// <summary>جزئیات یک بسته به‌همراه همه‌ی نسخه‌هایش (جدیدترین اول).</summary>
public sealed record PluginDetail(PluginSummary Latest, IReadOnlyList<PluginVersionInfo> Versions);

/// <summary>پاسخ صفحه‌بندی‌شده‌ی لیست بسته‌ها.</summary>
public sealed record PluginPage(int Total, int Page, int PageSize, IReadOnlyList<PluginSummary> Items);

/// <summary>نتیجه‌ی تلاش برای انتشار یک بسته.</summary>
public sealed record PublishResult(bool Ok, string? Error, PluginSummary? Plugin)
{
    public static PublishResult Fail(string error) => new(false, error, null);
    public static PublishResult Success(PluginSummary plugin) => new(true, null, plugin);
}
