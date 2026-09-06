using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace PlugLauncher.Core;

/// <summary>یک بسته در فروشگاه (همان چیزی که API فروشگاه برمی‌گرداند).</summary>
public sealed class StorePlugin
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;
    [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = [];
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("publishedAt")] public DateTimeOffset PublishedAt { get; set; }
    [JsonPropertyName("downloads")] public int Downloads { get; set; }
    [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = string.Empty;
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
}

/// <summary>پاسخ صفحه‌بندی‌شده‌ی فروشگاه.</summary>
public sealed class StorePage
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("pageSize")] public int PageSize { get; set; }
    [JsonPropertyName("items")] public List<StorePlugin> Items { get; set; } = [];
}

/// <summary>
/// کلاینت فروشگاه: گرفتن فهرست، دانلود بسته، بررسی صحت sha256 و نصب در پوشه‌ی پلاگین‌های کاربر.
/// هیچ‌وقت خارج از <c>%APPDATA%\PlugLauncher\plugins</c> چیزی نمی‌نویسد.
/// </summary>
public sealed class StoreClient(string baseUrl, FileLogger? logger = null) : IDisposable
{
    private readonly FileLogger _log = logger ?? new FileLogger("store");
    private readonly HttpClient _http = new()
    {
        // BaseAddress باید با اسلش تمام شود تا مسیرهای نسبی درست ضمیمه شوند
        BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string BaseUrl => _http.BaseAddress!.ToString();

    /// <summary>گرفتن فهرست بسته‌ها؛ <paramref name="query"/> خالی یعنی همه.</summary>
    public async Task<IReadOnlyList<StorePlugin>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(query)
            ? "api/v1/plugins?pageSize=100"
            : $"api/v1/plugins?pageSize=100&q={Uri.EscapeDataString(query.Trim())}";

        var page = await _http.GetFromJsonAsync<StorePage>(url, cancellationToken).ConfigureAwait(false);
        return page?.Items ?? [];
    }

    /// <summary>دانلود آیکن یک بسته در حافظه (برای نمایش در لیست فروشگاه).</summary>
    public async Task<byte[]?> DownloadIconAsync(StorePlugin plugin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plugin.IconUrl)) return null;

        try
        {
            return await _http.GetByteArrayAsync(plugin.IconUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warn($"could not download icon for \"{plugin.Id}\": {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// دانلود و نصب یک بسته. بسته اول در فایل موقت دانلود می‌شود، sha256 آن با مقدار اعلام‌شده‌ی
    /// فروشگاه مقایسه می‌شود و تنها در صورت تطابق در پوشه‌ی پلاگین کاربر باز می‌شود.
    /// </summary>
    public async Task<string> InstallAsync(StorePlugin plugin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plugin.Id)) throw new InvalidOperationException("The package id is empty.");

        var temp = Path.Combine(Path.GetTempPath(), $"plz-{Guid.NewGuid():N}.zip");

        try
        {
            var url = string.IsNullOrWhiteSpace(plugin.DownloadUrl)
                ? $"api/v1/plugins/{Uri.EscapeDataString(plugin.Id)}/download"
                : plugin.DownloadUrl;

            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            VerifyHash(temp, plugin);

            var target = Path.Combine(PluginPaths.UserPlugins, SanitizeId(plugin.Id));
            ExtractSafely(temp, target);

            _log.Info($"package \"{plugin.Id}\" v{plugin.Version} installed into {target}");
            return target;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>بررسی صحت فایل دانلودشده در برابر sha256 اعلام‌شده‌ی فروشگاه.</summary>
    private static void VerifyHash(string file, StorePlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.Sha256)) return;

        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        if (!actual.Equals(plugin.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The downloaded file does not match the store checksum (expected {plugin.Sha256}, got {actual}). Nothing was installed.");
    }

    /// <summary>
    /// باز کردن zip با محافظت در برابر zip slip: هر ورودی باید داخل پوشه‌ی مقصد بماند.
    /// نصب روی یک پوشه‌ی موقت انجام و در پایان جایگزین پوشه‌ی نهایی می‌شود تا نصب نیمه‌کاره نماند.
    /// </summary>
    private static void ExtractSafely(string packagePath, string targetDirectory)
    {
        var staging = targetDirectory + ".installing";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        try
        {
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var root = Path.GetFullPath(staging + Path.DirectorySeparatorChar);

                foreach (var entry in archive.Entries)
                {
                    // ورودی‌های پوشه‌ای طول صفر و نام پایان‌یافته به اسلش دارند
                    var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (relative.Length == 0) continue;

                    var destination = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"The package contains a path outside the target folder: {entry.FullName}");

                    if (relative.EndsWith('/') || string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            if (!File.Exists(Path.Combine(staging, "plugin.json")))
                throw new InvalidOperationException("The package has no plugin.json.");

            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            Directory.Move(staging, targetDirectory);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); } catch { /* پاک‌سازی بهترین‌تلاش */ }
            }

            throw;
        }
    }

    private static string SanitizeId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(id.Select(c => invalid.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(clean) ? "plugin" : clean;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // فایل موقت
        }
    }

    public void Dispose() => _http.Dispose();
}
