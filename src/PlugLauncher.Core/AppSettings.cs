using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlugLauncher.Core;

/// <summary>جای عمودی نوار جستجو روی نمایشگر فعال، یا مختصات صریح کاربر.</summary>
public enum LauncherBarPosition
{
    // مقدار صفر عمداً همان رفتار قدیمی است؛ فایل‌های تنظیمات قدیمی بدون مهاجرت همان‌جا می‌مانند.
    Top,
    Center,
    Bottom,
    Custom
}

/// <summary>سمتی از نوار جستجو که پاسخ‌ها در آن باز می‌شوند.</summary>
public enum ResultListPlacement
{
    Below,
    Above
}

/// <summary>تنظیمات کاربر؛ در <c>%APPDATA%\PlugLauncher\settings.json</c> ذخیره می‌شود.</summary>
public sealed class AppSettings
{
    public const int MinLauncherScalePercent = 50;
    public const int MaxLauncherScalePercent = 200;

    private int _launcherScalePercent = 100;

    /// <summary>هات‌کی سراسری نمایش پنجره، مثلاً <c>Alt+Space</c>.</summary>
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>
    /// جای نوار جستجو. Top همان جای تاریخی برنامه است: تقریباً یک‌پنجم پایین‌تر از بالای
    /// نمایشگر فعال؛ بنابراین افزودن این گزینه پنجره‌ی نصب‌های موجود را ناگهان جابه‌جا نمی‌کند.
    /// </summary>
    [JsonPropertyName("barPosition")]
    public LauncherBarPosition BarPosition { get; set; } = LauncherBarPosition.Top;

    /// <summary>مختصات پیکسلی لبه‌ی چپ نوار در حالت <see cref="LauncherBarPosition.Custom"/>.</summary>
    [JsonPropertyName("customBarX")]
    public int CustomBarX { get; set; }

    /// <summary>مختصات پیکسلی لبه‌ی بالای نوار در حالت <see cref="LauncherBarPosition.Custom"/>.</summary>
    [JsonPropertyName("customBarY")]
    public int CustomBarY { get; set; }

    /// <summary>فهرست پاسخ‌ها بالای نوار باز شود یا پایین آن.</summary>
    [JsonPropertyName("resultListPlacement")]
    public ResultListPlacement ResultListPlacement { get; set; } = ResultListPlacement.Below;

    /// <summary>
    /// مقیاس تمام رابط لانچر، بر حسب درصد. خودِ تنظیم مقدار را محدود می‌کند تا عدد خراب در فایل
    /// تنظیمات پنجره را نامرئی یا آن‌قدر بزرگ نکند که دیگر نتوان آن را اصلاح کرد.
    /// </summary>
    [JsonPropertyName("launcherScalePercent")]
    public int LauncherScalePercent
    {
        get => _launcherScalePercent;
        set => _launcherScalePercent = Math.Clamp(value, MinLauncherScalePercent, MaxLauncherScalePercent);
    }

    /// <summary>شناسه‌ی پلاگین‌هایی که کاربر غیرفعال کرده است.</summary>
    [JsonPropertyName("disabledPlugins")]
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>حداکثر تعداد ردیف نمایشی.</summary>
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 8;

    /// <summary>مهلت پاسخ هر پلاگین به یک کوئری (میلی‌ثانیه).</summary>
    [JsonPropertyName("queryTimeoutMs")]
    public int QueryTimeoutMs { get; set; } = 3000;

    /// <summary>اجرای خودکار هنگام ورود به ویندوز. منبع حقیقت خودِ رجیستری است و این مقدار آینه‌ی آن.</summary>
    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// آیا یک‌بار موقع اجرا از کاربر پرسیده شده که اجرای خودکار را روشن کند یا نه.
    /// بدون این، «نه» گفتن هیچ اثری نداشت و همان سؤال هر بار تکرار می‌شد.
    /// </summary>
    [JsonPropertyName("startupPromptAnswered")]
    public bool StartupPromptAnswered { get; set; }

    /// <summary>
    /// بررسی خودکار وجود نسخه‌ی جدید هنگام اجرا. تنها اثرش یک درخواست GET به API گیت‌هاب است،
    /// حداکثر روزی یک‌بار؛ هیچ چیزی دانلود یا نصب نمی‌شود.
    /// </summary>
    [JsonPropertyName("checkForUpdates")]
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// آخرین باری که بررسی <b>موفق</b> بود. فقط موفق‌ها ثبت می‌شوند، وگرنه کسی که موقع بوت
    /// آفلاین است تا یک روز دیگر هم بررسی نمی‌شود.
    /// </summary>
    [JsonPropertyName("lastUpdateCheckUtc")]
    public DateTime? LastUpdateCheckUtc { get; set; }

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
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // اسم‌ها در فایل خواناتر از عددند. مبدلِ خودمان مقدار خراب یا متعلق به نسخه‌ای تازه‌تر
            // را به پیش‌فرض enum برمی‌گرداند، به‌جای اینکه به‌خاطر یک گزینه همه‌ی تنظیمات کنار بروند.
            new FallbackStringEnumConverter<LauncherBarPosition>(),
            new FallbackStringEnumConverter<ResultListPlacement>()
        }
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

    /// <summary>
    /// نسخه‌ی مقاومِ JsonStringEnumConverter: مقدارهای شناخته‌شده را به camelCase می‌نویسد و
    /// ورودی ناشناخته را به عضو صفر enum می‌فرستد. عضو صفرِ enumهای تنظیمات همیشه پیش‌فرض امن است.
    /// </summary>
    private sealed class FallbackStringEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var parsed) &&
                Enum.IsDefined(parsed))
                return parsed;

            // عددهای نسخه‌ی آزمایشی اولیه را هم می‌خوانیم، ولی عدد ناشناخته وارد برنامه نمی‌شود.
            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt32(out var number) &&
                Enum.IsDefined(typeof(TEnum), number))
                return (TEnum)Enum.ToObject(typeof(TEnum), number);

            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject) reader.Skip();
            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (!Enum.IsDefined(value)) value = default;
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        }
    }
}
