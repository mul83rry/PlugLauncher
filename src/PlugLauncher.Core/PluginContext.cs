using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <inheritdoc cref="IPluginContext"/>
internal sealed class PluginContext(PluginManifest manifest, string pluginDirectory) : IPluginContext
{
    private string? _dataDirectory;

    public PluginManifest Manifest { get; } = manifest;
    public string PluginDirectory { get; } = pluginDirectory;

    public string DataDirectory
    {
        get
        {
            if (_dataDirectory is null)
            {
                _dataDirectory = PluginPaths.DataFor(Manifest.Id);
                Directory.CreateDirectory(_dataDirectory);
            }

            return _dataDirectory;
        }
    }

    public IPluginLogger Log { get; } = new FileLogger(manifest.Id);

    public string ResolvePath(string relativePath)
        => Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(PluginDirectory, relativePath);
}
