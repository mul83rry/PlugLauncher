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

    /// <summary>ارجاعات NuGet یا اسمبلی که اسکریپت لازم دارد (فاز بعد).</summary>
    [JsonPropertyName("references")]
    public List<string> References { get; set; } = [];
}
