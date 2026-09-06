namespace PlugLauncher.Contracts;

/// <summary>
/// قرارداد اصلی پلاگین. یک اسکریپت <c>main.csx</c> باید در انتها نمونه‌ای از این اینترفیس را
/// return کند (یا با <see cref="Plugin.Create"/> بسازد). پلاگین‌های کامپایل‌شده (DLL) هم
/// می‌توانند همین اینترفیس را پیاده‌سازی کنند.
/// </summary>
public interface IPlugin
{
    /// <summary>یک‌بار بعد از لود شدن پلاگین صدا زده می‌شود.</summary>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>برای هر تغییر متن جستجو صدا زده می‌شود.</summary>
    Task<IReadOnlyList<PluginResult>> QueryAsync(PluginQuery query, CancellationToken cancellationToken);
}
