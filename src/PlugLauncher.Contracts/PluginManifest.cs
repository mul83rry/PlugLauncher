using System.Text.Json.Serialization;

namespace PlugLauncher.Contracts;

/// <summary>محتوای فایل <c>plugin.json</c> در ریشه‌ی پوشه‌ی هر پلاگین.</summary>
public sealed class PluginManifest
{
    /// <summary>شناسه‌ی یکتا و پایدار پلاگین (مثلاً <c>com.hossein.steam-games</c>).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>فایل ورودی اسکریپت، نسبی نسبت به پوشه‌ی پلاگین.</summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "main.csx";

    /// <summary>
    /// کلیدواژه‌هایی که پلاگین را صراحتاً فعال می‌کنند (مثلاً <c>st</c> برای Steam).
    /// اگر خالی باشد، پلاگین برای همه‌ی کوئری‌ها صدا زده می‌شود.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];

    /// <summary>آیکن پیش‌فرض پلاگین، نسبی نسبت به پوشه‌ی پلاگین.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// نمونه‌های استفاده. وقتی کاربر کلیدواژه را تایپ کرده و بعدش هنوز چیزی ننوشته — همان لحظه‌ای
    /// که نمی‌داند چه گزینه‌هایی هست — این خط‌ها ته لیست نشان داده می‌شوند و Enter روی هرکدام
    /// نمونه را داخل باکس جستجو می‌گذارد. پس نحو هیچ پلاگینی لازم نیست حفظ شود.
    /// </summary>
    [JsonPropertyName("usage")]
    public List<PluginUsage> Usage { get; set; } = [];

    /// <summary>
    /// سیستم‌عامل‌هایی که این پلاگین روی آن‌ها کار می‌کند: <c>windows</c>، <c>macos</c>، <c>linux</c>.
    ///
    /// خالی یعنی همه‌جا، و این پیش‌فرضِ درستی است: پلاگینی که فقط متن را زیر و رو می‌کند هرجا کار
    /// می‌کند. این فیلد برای پلاگینی است که واقعاً به یک سیستم گره خورده — رجیستری، منوی استارت،
    /// یک exe مشخص — تا روی سیستم دیگر به‌جای شکستنِ بی‌توضیح، اصلاً پیشنهاد نشود.
    /// </summary>
    [JsonPropertyName("platforms")]
    public List<string> Platforms { get; set; } = [];

    /// <summary>
    /// قدیمی‌ترین نسخه‌ی لانچری که این پلاگین روی آن کار می‌کند، مثل <c>1.5.0</c>. خالی یعنی هر نسخه‌ای.
    ///
    /// اسکریپت پلاگین موقع لود کامپایل می‌شود، پس استفاده از چیزی که در هسته‌ی قدیمی‌تر وجود ندارد
    /// خطای کامپایل می‌دهد نه یک قابلیتِ غایب. این فیلد همان را از قبل می‌گوید.
    /// </summary>
    [JsonPropertyName("minCore")]
    public string MinCore { get; set; } = string.Empty;

    /// <summary>ارجاعات NuGet یا اسمبلی که اسکریپت لازم دارد (فاز بعد).</summary>
    [JsonPropertyName("references")]
    public List<string> References { get; set; } = [];
}

/// <summary>یک خط راهنما در <c>plugin.json</c>: نمونه‌ی کاملِ قابل‌اجرا و توضیح کوتاهش.</summary>
public sealed class PluginUsage
{
    /// <summary>متن دقیقی که با Enter داخل باکس جستجو می‌نشیند، شامل خود کلیدواژه.</summary>
    [JsonPropertyName("example")]
    public string Example { get; set; } = string.Empty;

    /// <summary>توضیح یک‌خطی که زیر نمونه می‌آید.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
