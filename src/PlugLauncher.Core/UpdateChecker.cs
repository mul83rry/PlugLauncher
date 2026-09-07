using System.Reflection;
using System.Text.Json;

namespace PlugLauncher.Core;

/// <summary>نسخه‌ای که روی گیت‌هاب منتشر شده و از نسخه‌ی در حال اجرا جدیدتر است.</summary>
/// <param name="Version">نسخه‌ی نرمال‌شده‌ی چهارجزئی — همان چیزی که مقایسه رویش انجام شده.</param>
/// <param name="Tag">تگ خام ریلیز، مثل <c>v1.4.0</c>.</param>
/// <param name="ReleaseUrl">صفحه‌ی ریلیز در گیت‌هاب؛ فایل‌های نصب و zip هر دو آنجا هستند.</param>
public sealed record UpdateInfo(Version Version, string Tag, string Name, string ReleaseUrl, DateTimeOffset PublishedAt);

/// <summary>
/// نتیجه‌ی یک بررسی. <see cref="Succeeded"/> جدا از <see cref="Update"/> است چون «نتوانستم به
/// گیت‌هاب برسم» با «به‌روز هستی» یکی نیست و نباید مثل هم گزارش یا ثبت شود.
/// </summary>
public sealed record UpdateCheckResult(bool Succeeded, UpdateInfo? Update, string? Error = null);

/// <summary>
/// بررسی وجود نسخه‌ی جدید از روی «آخرین ریلیز» مخزن گیت‌هاب.
///
/// هیچ داده‌ای فرستاده نمی‌شود و هیچ چیزی دانلود یا نصب نمی‌شود: فقط یک GET به
/// <c>api.github.com/repos/&lt;repo&gt;/releases/latest</c> و مقایسه‌ی شماره‌ی تگ با نسخه‌ی در حال اجرا.
/// نصب عمداً به عهده‌ی کاربر است، چون بسته به اینکه با installer نصب شده باشد یا zip را باز کرده
/// باشد، مسیر به‌روزرسانی فرق می‌کند و برنامه نمی‌تواند حدس بزند کدام است.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    /// <summary>مخزنی که ریلیزها از آن خوانده می‌شوند. مثل آدرس فروشگاه، در UI قابل ویرایش نیست.</summary>
    public const string DefaultRepository = "mul83rry/PlugLauncher";

    /// <summary>فاصله‌ی بررسی خودکار. روزی یک‌بار؛ ریلیز روزی چند بار نمی‌آید.</summary>
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromHours(24);

    private readonly FileLogger _log;
    private readonly Version _current;
    private readonly HttpClient _http;
    private readonly string _latestReleaseApi;

    public UpdateChecker(Version currentVersion, string repository = DefaultRepository, FileLogger? logger = null)
    {
        _log = logger ?? new FileLogger("update");
        _current = Normalize(currentVersion);

        var repo = repository.Trim().Trim('/');
        _latestReleaseApi = $"https://api.github.com/repos/{repo}/releases/latest";
        ReleasesPageUrl = $"https://github.com/{repo}/releases/latest";

        // ۳۰ ثانیه مثل StoreClient. سخاوتمندانه است چون این کار در پس‌زمینه انجام می‌شود و
        // هیچ‌کس منتظرش نیست؛ اولین اتصال TLS روی شبکه‌ی کند خودش چند ثانیه می‌برد.
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // بدون User-Agent، API گیت‌هاب با 403 جواب می‌دهد — این یکی اختیاری نیست.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"PlugLauncher/{_current.ToString(3)}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>صفحه‌ای که وقتی چیزی از پاسخ خوانده نشد به کاربر نشان داده می‌شود.</summary>
    public string ReleasesPageUrl { get; }

    /// <summary>نسخه‌ی در حال اجرا؛ همان <c>&lt;Version&gt;</c> در <c>Directory.Build.props</c>.</summary>
    public static Version RunningVersion
        => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// یک بررسی. هیچ‌وقت پرتاب نمی‌کند مگر خود <paramref name="cancellationToken"/> لغو شده باشد —
    /// آفلاین بودن یا فیلتر بودن گیت‌هاب خطای کاربر نیست و نباید استارتاپ را به هم بزند.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        string json;

        try
        {
            using var response = await _http.GetAsync(_latestReleaseApi, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var reason = $"GitHub answered {(int)response.StatusCode}";
                _log.Warn($"update check: {reason}");
                return new UpdateCheckResult(false, null, reason);
            }

            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // تایم‌اوت HttpClient هم TaskCanceledException می‌دهد، پس لغو واقعی را باید جدا تشخیص داد
            cancellationToken.ThrowIfCancellationRequested();

            _log.Warn($"update check failed: {ex.Message}");
            return new UpdateCheckResult(false, null, ex.Message);
        }

        return Parse(json);
    }

    private UpdateCheckResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = Text(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
                return new UpdateCheckResult(false, null, "the release has no tag");

            if (!TryParseVersion(tag, out var latest))
            {
                _log.Warn($"update check: tag \"{tag}\" is not a version number");
                return new UpdateCheckResult(false, null, $"\"{tag}\" is not a version number");
            }

            if (latest <= _current)
            {
                _log.Info($"update check: {_current.ToString(3)} is current (latest release is {tag})");
                return new UpdateCheckResult(true, null);
            }

            var url = Text(root, "html_url");
            var name = Text(root, "name");
            var published = root.TryGetProperty("published_at", out var at) && at.TryGetDateTimeOffset(out var when)
                ? when
                : DateTimeOffset.MinValue;

            _log.Info($"update check: {tag} is newer than {_current.ToString(3)}");

            return new UpdateCheckResult(true, new UpdateInfo(
                latest,
                tag,
                string.IsNullOrWhiteSpace(name) ? tag : name,
                string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url,
                published));
        }
        catch (JsonException ex)
        {
            _log.Warn($"update check: could not read the response: {ex.Message}");
            return new UpdateCheckResult(false, null, "the response could not be read");
        }
    }

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>«v1.4.0» و «1.4.0» هر دو. پسوندهایی مثل «-beta» یا «+sha» بریده می‌شوند.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) trimmed = trimmed[..cut];

        if (!Version.TryParse(trimmed, out var parsed)) return false;

        version = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// چهارجزئی کردن. بدون این، «1.3.0» با <c>Revision = -1</c> پارس می‌شود و از «1.3.0.0»
    /// کوچک‌تر حساب می‌شود — یعنی برنامه هر بار همان نسخه‌ی خودش را «جدید» اعلام می‌کرد.
    /// </summary>
    private static Version Normalize(Version value)
        => new(value.Major, value.Minor, Math.Max(value.Build, 0), Math.Max(value.Revision, 0));

    public void Dispose() => _http.Dispose();
}
