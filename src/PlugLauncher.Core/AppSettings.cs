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
                if (settings is not null) return settings;
            }
        }
        catch (Exception ex)
        {
            _log.Error("خواندن settings.json شکست خورد، مقادیر پیش‌فرض استفاده می‌شود", ex);
        }

        return new AppSettings();
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
            _log.Error("ذخیره‌ی settings.json شکست خورد", ex);
        }
    }
}
