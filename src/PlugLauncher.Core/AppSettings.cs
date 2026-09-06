using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlugLauncher.Core;

/// <summary>تنظیمات کاربر؛ در <c>%APPDATA%\PlugLauncher\settings.json</c> ذخیره می‌شود.</summary>
public sealed class AppSettings
{
    /// <summary>هات‌کی سراسری نمایش پنجره، مثلاً <c>Alt+Space</c>.</summary>
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>شناسه‌ی پلاگین‌هایی که کاربر غیرفعال کرده است.</summary>
    [JsonPropertyName("disabledPlugins")]
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>حداکثر تعداد ردیف نمایشی.</summary>
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 8;

    /// <summary>مهلت پاسخ هر پلاگین به یک کوئری (میلی‌ثانیه).</summary>
    [JsonPropertyName("queryTimeoutMs")]
    public int QueryTimeoutMs { get; set; } = 3000;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    /// <summary>آدرس پیش‌فرض فروشگاه (بدون اسلش پایانی؛ <c>StoreClient</c> خودش اضافه می‌کند).</summary>
    public const string DefaultStoreUrl = "https://mul83rry.github.io/PlugLauncher";

    /// <summary>آدرس فروشگاه قدیمی (VPS)؛ فقط برای ارتقای یک‌بارِ تنظیمات ذخیره‌شده نگه داشته شده است.</summary>
    public const string LegacyStoreUrl = "https://thehokm.cloud/pluglauncher";

    /// <summary>
    /// آدرس فروشگاه پلاگین. عمداً در UI قابل ویرایش نیست — مقدار پیش‌فرض همین‌جا هاردکد است و
    /// تغییرش فقط با ویرایش دستی <c>settings.json</c> ممکن است، تا کاربر ناخواسته به مخزن دیگری وصل نشود.
    /// </summary>
    [JsonPropertyName("storeUrl")]
    public string StoreUrl { get; set; } = DefaultStoreUrl;

    public bool IsEnabled(string pluginId)
        => !DisabledPlugins.Contains(pluginId, StringComparer.OrdinalIgnoreCase);
}

/// <summary>خواندن/نوشتن <see cref="AppSettings"/> روی دیسک.</summary>
public sealed class SettingsStore(FileLogger? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly FileLogger _log = logger ?? new FileLogger("settings");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(PluginPaths.SettingsFile))
            {
                var json = File.ReadAllText(PluginPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    if (MigrateStoreUrl(settings)) Save(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("could not read settings.json, falling back to defaults", ex);
        }

        return new AppSettings();
    }

    /// <summary>
    /// نصب‌های موجود آدرس فروشگاه قدیمی را در <c>settings.json</c> ذخیره دارند و عوض شدن پیش‌فرض در کد
    /// آن‌ها را جابه‌جا نمی‌کند. فقط وقتی مقدار <b>دقیقاً</b> همان آدرس قدیمی است ارتقا داده می‌شود، تا
    /// کاربری که عمداً آدرس دیگری گذاشته دست نخورد.
    /// </summary>
    private bool MigrateStoreUrl(AppSettings settings)
    {
        var current = settings.StoreUrl?.TrimEnd('/');
        if (!string.Equals(current, AppSettings.LegacyStoreUrl, StringComparison.OrdinalIgnoreCase)) return false;

        settings.StoreUrl = AppSettings.DefaultStoreUrl;
        _log.Info($"store url migrated from {AppSettings.LegacyStoreUrl} to {AppSettings.DefaultStoreUrl}");
        return true;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PluginPaths.SettingsFile)!);
            File.WriteAllText(PluginPaths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.Error("could not save settings.json", ex);
        }
    }
}
