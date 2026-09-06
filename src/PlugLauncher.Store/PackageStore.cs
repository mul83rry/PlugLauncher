using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlugLauncher.Contracts;

namespace PlugLauncher.Store;

/// <summary>
/// انبار بسته‌ها روی دیسک به‌همراه فهرست درون‌حافظه‌ای.
///
/// ساختار روی دیسک:
/// <code>
/// {DataRoot}/packages/{pluginId}/{version}/package.plz   # خود بسته
///                                          plugin.json    # مانیفست استخراج‌شده
///                                          meta.json      # sha256، حجم، زمان انتشار
///                                          icon.*         # آیکن استخراج‌شده (در صورت وجود)
/// {DataRoot}/stats.json                                   # شمارنده‌ی دانلود
/// </code>
/// دیتابیس لازم ندارد؛ فهرست در استارتاپ با اسکن همین ساختار ساخته می‌شود.
/// </summary>
public sealed class PackageStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly StoreOptions _options;
    private readonly ILogger<PackageStore> _log;
    private readonly ConcurrentDictionary<string, PluginRecord> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _writeGate = new();
    private readonly Timer _statsTimer;
    private int _statsDirty;

    public PackageStore(IOptions<StoreOptions> options, ILogger<PackageStore> log)
    {
        _options = options.Value;
        _log = log;

        Directory.CreateDirectory(PackagesRoot);
        LoadStats();
        Rescan();

        _statsTimer = new Timer(_ => FlushStats(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private string PackagesRoot => Path.Combine(_options.DataRoot, "packages");
    private string StatsFile => Path.Combine(_options.DataRoot, "stats.json");

    public int PluginCount => _plugins.Count;
    public int VersionCount => _plugins.Values.Sum(p => p.Versions.Count);

    // ===== خواندن فهرست =====

    /// <summary>جستجو و صفحه‌بندی روی آخرین نسخه‌ی هر بسته.</summary>
    public PluginPage Search(string? query, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var all = _plugins.Values
            .Select(p => p.Latest)
            .Where(v => v is not null)
            .Select(v => ToSummary(v!))
            .ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            all = all
                .Where(p =>
                    p.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    p.Author.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    p.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var ordered = all
            .OrderByDescending(p => p.Downloads)
            .ThenByDescending(p => p.PublishedAt)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PluginPage(ordered.Count, page, pageSize, items);
    }

    public PluginDetail? GetDetail(string id)
    {
        if (!_plugins.TryGetValue(id, out var record) || record.Latest is null) return null;

        var versions = record.Versions.Values
            .OrderByDescending(v => v.Version)
            .Select(v => new PluginVersionInfo(
                v.Version.Raw,
                v.Size,
                v.Sha256,
                v.PublishedAt,
                DownloadUrlOf(record.Id, v.Version.Raw)))
            .ToList();

        return new PluginDetail(ToSummary(record.Latest), versions);
    }

    /// <summary>باز کردن فایل بسته برای دانلود؛ اگر نسخه مشخص نشود، آخرین نسخه.</summary>
    public (Stream Stream, string FileName)? OpenPackage(string id, string? version)
    {
        var record = Resolve(id, version);
        if (record is null) return null;

        var path = Path.Combine(record.Directory, "package.plz");
        if (!File.Exists(path)) return null;

        RecordDownload(id);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return (stream, $"{id}-{record.Version.Raw}.plz");
    }

    /// <summary>باز کردن آیکن بسته برای نمایش در فروشگاه.</summary>
    public (Stream Stream, string ContentType)? OpenIcon(string id, string? version)
    {
        var record = Resolve(id, version);
        if (record?.IconFile is null) return null;

        var path = Path.Combine(record.Directory, record.IconFile);
        if (!File.Exists(path)) return null;

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, ContentTypeOf(record.IconFile));
    }

    // ===== نوشتن =====

    /// <summary>پذیرش یک بسته‌ی جدید. جریان ورودی روی فایل موقت نوشته و بعد اعتبارسنجی می‌شود.</summary>
    public async Task<PublishResult> PublishAsync(Stream body, bool overwrite, CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"plz-{Guid.NewGuid():N}.zip");

        try
        {
            long size;
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                size = await CopyLimitedAsync(body, file, _options.MaxPackageBytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (size < 0)
                return PublishResult.Fail($"حجم بسته از حد مجاز ({_options.MaxPackageBytes / (1024 * 1024)}MB) بیشتر است.");

            if (size == 0) return PublishResult.Fail("بدنه‌ی درخواست خالی است.");

            using var archive = OpenArchive(temp, out var openError);
            if (archive is null) return PublishResult.Fail(openError);

            if (!PackageValidator.TryValidate(archive, _options, out var manifest, out var version, out var error))
                return PublishResult.Fail(error);

            var targetDirectory = VersionDirectory(manifest.Id, version.Raw);
            if (Directory.Exists(targetDirectory) && !overwrite)
                return PublishResult.Fail($"نسخه‌ی {version.Raw} از «{manifest.Id}» از قبل منتشر شده است.");

            var record = WriteVersionFiles(archive, manifest, version, temp, size);

            var plugin = _plugins.GetOrAdd(manifest.Id, id => new PluginRecord(id));
            plugin.Versions[version.Raw] = record;

            _log.LogInformation("بسته منتشر شد: {Id} v{Version} ({Size} بایت)", manifest.Id, version.Raw, size);
            return PublishResult.Success(ToSummary(record));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "انتشار بسته شکست خورد");
            return PublishResult.Fail($"خطای داخلی هنگام انتشار: {ex.Message}");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>حذف یک نسخه، یا کل بسته وقتی نسخه مشخص نشود.</summary>
    public bool Delete(string id, string? version)
    {
        lock (_writeGate)
        {
            if (!_plugins.TryGetValue(id, out var plugin)) return false;

            if (string.IsNullOrWhiteSpace(version))
            {
                var directory = Path.Combine(PackagesRoot, id);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);

                _plugins.TryRemove(id, out _);
                _downloads.TryRemove(id, out _);
                Interlocked.Exchange(ref _statsDirty, 1);
                _log.LogInformation("بسته حذف شد: {Id}", id);
                return true;
            }

            if (!plugin.Versions.TryRemove(version.Trim(), out var record)) return false;

            if (Directory.Exists(record.Directory)) Directory.Delete(record.Directory, recursive: true);

            if (plugin.Versions.IsEmpty)
            {
                _plugins.TryRemove(id, out _);
                var directory = Path.Combine(PackagesRoot, id);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }

            _log.LogInformation("نسخه حذف شد: {Id} v{Version}", id, version);
            return true;
        }
    }

    /// <summary>ساخت دوباره‌ی فهرست از روی دیسک (بعد از تغییر دستی فایل‌ها).</summary>
    public void Rescan()
    {
        _plugins.Clear();
        if (!Directory.Exists(PackagesRoot)) return;

        foreach (var pluginDirectory in Directory.EnumerateDirectories(PackagesRoot))
        {
            var id = Path.GetFileName(pluginDirectory);
            var plugin = new PluginRecord(id);

            foreach (var versionDirectory in Directory.EnumerateDirectories(pluginDirectory))
            {
                var record = TryReadVersion(id, versionDirectory);
                if (record is not null) plugin.Versions[record.Version.Raw] = record;
            }

            if (!plugin.Versions.IsEmpty) _plugins[id] = plugin;
        }

        _log.LogInformation("فهرست فروشگاه ساخته شد: {Plugins} بسته، {Versions} نسخه", PluginCount, VersionCount);
    }

    // ===== کمکی‌ها =====

    private VersionRecord? Resolve(string id, string? version)
    {
        if (!_plugins.TryGetValue(id, out var plugin)) return null;

        if (string.IsNullOrWhiteSpace(version)) return plugin.Latest;
        return plugin.Versions.TryGetValue(version.Trim(), out var record) ? record : null;
    }

    private string VersionDirectory(string id, string version) => Path.Combine(PackagesRoot, id, version);

    private static string DownloadUrlOf(string id, string version)
        => $"api/v1/plugins/{Uri.EscapeDataString(id)}/download?version={Uri.EscapeDataString(version)}";

    private PluginSummary ToSummary(VersionRecord record) => new(
        record.Manifest.Id,
        string.IsNullOrWhiteSpace(record.Manifest.Name) ? record.Manifest.Id : record.Manifest.Name,
        record.Manifest.Description,
        record.Version.Raw,
        record.Manifest.Author,
        record.Manifest.Keywords,
        record.Size,
        record.Sha256,
        record.PublishedAt,
        _downloads.GetValueOrDefault(record.Manifest.Id),
        DownloadUrlOf(record.Manifest.Id, record.Version.Raw),
        record.IconFile is null ? null : $"api/v1/plugins/{Uri.EscapeDataString(record.Manifest.Id)}/icon");

    private static ZipArchive? OpenArchive(string path, out string error)
    {
        error = string.Empty;
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (Exception ex)
        {
            error = $"فایل یک zip معتبر نیست: {ex.Message}";
            return null;
        }
    }

    /// <summary>نوشتن فایل‌های یک نسخه روی دیسک.</summary>
    private VersionRecord WriteVersionFiles(
        ZipArchive archive,
        PluginManifest manifest,
        PackageVersion version,
        string packagePath,
        long size)
    {
        lock (_writeGate)
        {
            var directory = VersionDirectory(manifest.Id, version.Raw);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            Directory.CreateDirectory(directory);

            File.Copy(packagePath, Path.Combine(directory, "package.plz"), overwrite: true);

            var manifestEntry = PackageValidator.FindEntry(archive, "plugin.json")!;
            manifestEntry.ExtractToFile(Path.Combine(directory, "plugin.json"), overwrite: true);

            string? iconFile = null;
            if (!string.IsNullOrWhiteSpace(manifest.Icon))
            {
                var iconEntry = PackageValidator.FindEntry(archive, manifest.Icon);
                if (iconEntry is not null)
                {
                    var extension = Path.GetExtension(iconEntry.Name);
                    iconFile = string.IsNullOrEmpty(extension) ? "icon.png" : $"icon{extension}";
                    iconEntry.ExtractToFile(Path.Combine(directory, iconFile), overwrite: true);
                }
            }

            var meta = new PackageMeta
            {
                Sha256 = ComputeSha256(packagePath),
                Size = size,
                PublishedAt = DateTimeOffset.UtcNow,
                IconFile = iconFile
            };

            File.WriteAllText(Path.Combine(directory, "meta.json"), JsonSerializer.Serialize(meta, JsonOptions));

            return new VersionRecord(
                manifest, version, meta.Size, meta.Sha256, meta.PublishedAt, iconFile, directory);
        }
    }

    private VersionRecord? TryReadVersion(string id, string versionDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(versionDirectory, "plugin.json");
            var metaPath = Path.Combine(versionDirectory, "meta.json");
            if (!File.Exists(manifestPath) || !File.Exists(metaPath)) return null;

            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
            var meta = JsonSerializer.Deserialize<PackageMeta>(File.ReadAllText(metaPath), JsonOptions);
            if (manifest is null || meta is null) return null;

            if (!PackageVersion.TryParse(manifest.Version, out var version)) return null;

            return new VersionRecord(
                manifest, version, meta.Size, meta.Sha256, meta.PublishedAt, meta.IconFile, versionDirectory);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "خواندن نسخه‌ی {Directory} از بسته‌ی {Id} شکست خورد", versionDirectory, id);
            return null;
        }
    }

    /// <summary>کپی با سقف حجم؛ اگر از سقف عبور کند 1- برمی‌گرداند.</summary>
    private static async Task<long> CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) break;

            total += read;
            if (total > limit) return -1;

            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }

        return total;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };

    private void RecordDownload(string id)
    {
        _downloads.AddOrUpdate(id, 1, (_, count) => count + 1);
        Interlocked.Exchange(ref _statsDirty, 1);
    }

    private void LoadStats()
    {
        try
        {
            if (!File.Exists(StatsFile)) return;

            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(StatsFile), JsonOptions);
            if (data is null) return;

            foreach (var (key, value) in data) _downloads[key] = value;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "خواندن stats.json شکست خورد");
        }
    }

    public void FlushStats()
    {
        if (Interlocked.Exchange(ref _statsDirty, 0) == 0) return;

        try
        {
            Directory.CreateDirectory(_options.DataRoot);
            var snapshot = _downloads.ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(StatsFile, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ذخیره‌ی stats.json شکست خورد");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // فایل موقت؛ اگر پاک نشد سیستم‌عامل بعداً پاکش می‌کند
        }
    }

    public void Dispose()
    {
        _statsTimer.Dispose();
        FlushStats();
    }

    // ===== مدل‌های داخلی =====

    private sealed class PluginRecord(string id)
    {
        public string Id { get; } = id;
        public ConcurrentDictionary<string, VersionRecord> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>آخرین نسخه بر اساس مقایسه‌ی semver.</summary>
        public VersionRecord? Latest => Versions.Values.OrderByDescending(v => v.Version).FirstOrDefault();
    }

    private sealed record VersionRecord(
        PluginManifest Manifest,
        PackageVersion Version,
        long Size,
        string Sha256,
        DateTimeOffset PublishedAt,
        string? IconFile,
        string Directory);

    private sealed class PackageMeta
    {
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public string? IconFile { get; set; }
    }
}
