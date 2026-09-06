using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PlugLauncher.Contracts;

namespace PlugLauncher.Store;

/// <summary>
/// اعتبارسنجی فایل بسته (<c>.plz</c> که یک zip معمولی است) قبل از پذیرش در فروشگاه.
/// هدف: هیچ بسته‌ای که کلاینت نتواند نصب کند یا برای کلاینت خطرناک باشد وارد فروشگاه نشود.
/// </summary>
public static partial class PackageValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{2,99}$")]
    private static partial Regex PluginIdPattern();

    /// <summary>بررسی کامل بسته؛ در صورت معتبر بودن مانیفست را برمی‌گرداند.</summary>
    public static bool TryValidate(
        ZipArchive archive,
        StoreOptions options,
        out PluginManifest manifest,
        out PackageVersion version,
        out string error)
    {
        manifest = new PluginManifest();
        version = default;
        error = string.Empty;

        if (archive.Entries.Count == 0)
        {
            error = "بسته خالی است.";
            return false;
        }

        if (archive.Entries.Count > options.MaxEntries)
        {
            error = $"تعداد فایل‌های بسته ({archive.Entries.Count}) از حد مجاز ({options.MaxEntries}) بیشتر است.";
            return false;
        }

        long extracted = 0;
        foreach (var entry in archive.Entries)
        {
            var name = Normalize(entry.FullName);

            if (name.Length == 0)
            {
                error = "بسته شامل مسیر خالی است.";
                return false;
            }

            if (Path.IsPathRooted(name) || name.StartsWith('/') || name.Contains(':'))
            {
                error = $"مسیر مطلق در بسته مجاز نیست: {entry.FullName}";
                return false;
            }

            if (name.Split('/').Any(segment => segment == ".."))
            {
                error = $"مسیر بازگشتی (..) در بسته مجاز نیست: {entry.FullName}";
                return false;
            }

            extracted += entry.Length;
            if (extracted > options.MaxExtractedBytes)
            {
                error = $"حجم بازشده‌ی بسته از حد مجاز ({options.MaxExtractedBytes / (1024 * 1024)}MB) بیشتر است.";
                return false;
            }
        }

        var manifestEntry = FindEntry(archive, "plugin.json");
        if (manifestEntry is null)
        {
            error = "فایل plugin.json در ریشه‌ی بسته پیدا نشد.";
            return false;
        }

        try
        {
            using var stream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<PluginManifest>(stream, JsonOptions) ?? new PluginManifest();
        }
        catch (Exception ex)
        {
            error = $"plugin.json قابل خواندن نیست: {ex.Message}";
            return false;
        }

        if (!PluginIdPattern().IsMatch(manifest.Id))
        {
            error = "فیلد id نامعتبر است؛ فقط حروف/عدد/نقطه/خط‌تیره و بین ۳ تا ۱۰۰ کاراکتر مجاز است.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            error = "فیلد name خالی است.";
            return false;
        }

        if (!PackageVersion.TryParse(manifest.Version, out version))
        {
            error = $"فیلد version نامعتبر است: «{manifest.Version}» (قالب مورد انتظار: 1.0.0 یا 1.0.0-beta)";
            return false;
        }

        var entryFile = Normalize(manifest.Entry);
        if (entryFile.Length == 0)
        {
            error = "فیلد entry خالی است.";
            return false;
        }

        if (FindEntry(archive, entryFile) is null)
        {
            error = $"فایل ورودی «{manifest.Entry}» داخل بسته وجود ندارد.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Icon) && FindEntry(archive, Normalize(manifest.Icon)) is null)
        {
            error = $"آیکن «{manifest.Icon}» داخل بسته وجود ندارد.";
            return false;
        }

        return true;
    }

    /// <summary>پیدا کردن یک فایل داخل zip بدون حساسیت به بزرگی/کوچکی حروف و جهت اسلش.</summary>
    public static ZipArchiveEntry? FindEntry(ZipArchive archive, string relativePath)
    {
        var target = Normalize(relativePath);
        return archive.Entries.FirstOrDefault(e =>
            Normalize(e.FullName).Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
        => path.Replace('\\', '/').Trim().TrimStart('/');
}
