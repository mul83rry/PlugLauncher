namespace PlugLauncher.Contracts;

/// <summary>
/// کمک‌کننده برای ساخت سریع پلاگین در اسکریپت‌های <c>.csx</c> بدون نیاز به تعریف کلاس.
/// اسکریپت باید خروجی این متدها را در انتها <c>return</c> کند.
/// </summary>
public static class Plugin
{
    /// <summary>ساخت پلاگین از یک تابع همگام.</summary>
    public static IPlugin Create(Func<PluginQuery, IEnumerable<PluginResult>> query)
        => new DelegatePlugin((q, _) => Task.FromResult<IReadOnlyList<PluginResult>>(query(q).ToList()), null);

    /// <summary>ساخت پلاگین از یک تابع ناهمگام.</summary>
    public static IPlugin Create(Func<PluginQuery, CancellationToken, Task<IReadOnlyList<PluginResult>>> query)
        => new DelegatePlugin(query, null);

    /// <summary>ساخت پلاگین با مقداردهی اولیه (مثلاً خواندن یک فایل یا کش کردن لیستی در حافظه).</summary>
    public static IPlugin Create(
        Func<PluginQuery, CancellationToken, Task<IReadOnlyList<PluginResult>>> query,
        Func<IPluginContext, CancellationToken, Task> initialize)
        => new DelegatePlugin(query, initialize);

    private sealed class DelegatePlugin(
        Func<PluginQuery, CancellationToken, Task<IReadOnlyList<PluginResult>>> query,
        Func<IPluginContext, CancellationToken, Task>? initialize) : IPlugin
    {
        public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
            => initialize?.Invoke(context, cancellationToken) ?? Task.CompletedTask;

        public Task<IReadOnlyList<PluginResult>> QueryAsync(PluginQuery q, CancellationToken cancellationToken)
            => query(q, cancellationToken);
    }
}
