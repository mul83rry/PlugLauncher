namespace PlugLauncher.Contracts;

/// <summary>سرویس‌هایی که میزبان در اختیار پلاگین می‌گذارد.</summary>
public interface IPluginContext
{
    /// <summary>مانیفست همین پلاگین.</summary>
    PluginManifest Manifest { get; }

    /// <summary>مسیر مطلق پوشه‌ی پلاگین (جایی که <c>plugin.json</c> و <c>assets/</c> هستند).</summary>
    string PluginDirectory { get; }

    /// <summary>پوشه‌ی مخصوص داده‌های این پلاگین در <c>%APPDATA%</c>؛ در اولین دسترسی ساخته می‌شود.</summary>
    string DataDirectory { get; }

    /// <summary>لاگ‌گیری ساده برای عیب‌یابی پلاگین.</summary>
    IPluginLogger Log { get; }

    /// <summary>تبدیل مسیر نسبی (نسبت به پوشه‌ی پلاگین) به مسیر مطلق.</summary>
    string ResolvePath(string relativePath);
}

/// <summary>لاگ‌گیری پلاگین؛ خروجی در فایل لاگ میزبان جمع می‌شود.</summary>
public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
