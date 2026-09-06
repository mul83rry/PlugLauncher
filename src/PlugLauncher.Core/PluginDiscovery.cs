using System.Text.Json;
using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <summary>پوشه‌های پلاگین را اسکن می‌کند و <c>plugin.json</c> هرکدام را می‌خواند و اعتبارسنجی می‌کند.</summary>
public sealed class PluginDiscovery(FileLogger? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly FileLogger _log = logger ?? new FileLogger("discovery");

    /// <summary>
    /// همه‌ی پلاگین‌های معتبر را برمی‌گرداند. اگر یک پلاگین در چند ریشه باشد،
    /// اولین ریشه (پوشه‌ی کاربر) برنده است.
    /// </summary>
    public IReadOnlyList<PluginDescriptor> Discover()
    {
        var byId = new Dictionary<string, PluginDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in PluginPaths.PluginRoots())
        {
            if (!Directory.Exists(root)) continue;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var descriptor = TryRead(directory);
                if (descriptor is null) continue;

                if (!byId.TryAdd(descriptor.Id, descriptor))
                    _log.Warn($"پلاگین تکراری نادیده گرفته شد: {descriptor.Id} در {directory}");
            }
        }

        return byId.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>خواندن یک پوشه‌ی پلاگین؛ در صورت نامعتبر بودن null برمی‌گرداند و دلیل را لاگ می‌کند.</summary>
    public PluginDescriptor? TryRead(string directory)
    {
        var manifestPath = Path.Combine(directory, "plugin.json");
        if (!File.Exists(manifestPath)) return null;

        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception ex)
        {
            _log.Error($"plugin.json نامعتبر است: {manifestPath}", ex);
            return null;
        }

        if (manifest is null)
        {
            _log.Error($"plugin.json خالی است: {manifestPath}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            _log.Error($"plugin.json فیلد id ندارد: {manifestPath}");
            return null;
        }

        var entry = Path.Combine(directory, manifest.Entry);
        if (!File.Exists(entry))
        {
            _log.Error($"فایل ورودی پلاگین «{manifest.Id}» پیدا نشد: {entry}");
            return null;
        }

        return new PluginDescriptor
        {
            Manifest = manifest,
            Directory = Path.GetFullPath(directory),
            EntryFile = Path.GetFullPath(entry)
        };
    }
}
