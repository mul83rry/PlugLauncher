using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using PlugLauncher.Contracts;

namespace PlugLauncher.PluginUI;

/// <summary>
/// A launcher-themed top-level window for a plugin. Plugins can use ordinary Avalonia controls
/// for the content; <see cref="PluginWindows"/> takes care of dispatch and window lifetime.
/// </summary>
public class PluginWindow : Window
{
    public PluginWindow()
    {
        Width = 720;
        Height = 520;
        MinWidth = 380;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = ResourceBrush("WindowBrush", Color.FromRgb(0x1B, 0x1B, 0x1B));
    }

    /// <summary>Gets a brush from the active launcher theme, with a safe fallback.</summary>
    public static IBrush ResourceBrush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}

/// <summary>
/// Opens and tracks windows owned by plugins. Each plugin gets its own key space, repeated calls
/// activate the existing window, and every UI operation is marshalled to Avalonia's UI thread.
/// </summary>
public static class PluginWindows
{
    private static readonly Dictionary<string, Window> OpenWindows = new(StringComparer.Ordinal);

    /// <summary>Creates a window scope whose keys are isolated to this plugin.</summary>
    public static PluginWindowScope For(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new PluginWindowScope(context.Manifest.Id);
    }

    internal static void Show(string pluginId, string key, Func<Window> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(create);

        RunOnUiThread(() => ShowCore(FullKey(pluginId, key), create));
    }

    /// <summary>Closes every window created by one plugin, for disable, update, or uninstall.</summary>
    public static void CloseFor(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        RunOnUiThread(() => CloseWhere(key => key.StartsWith(pluginId + "\0", StringComparison.Ordinal)));
    }

    /// <summary>Closes all plugin windows. The host calls this during reload and shutdown.</summary>
    public static void CloseAll() => RunOnUiThread(() => CloseWhere(_ => true));

    private static void ShowCore(string fullKey, Func<Window> create)
    {
        if (OpenWindows.TryGetValue(fullKey, out var existing))
        {
            if (!existing.IsVisible) existing.Show();
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = create() ?? throw new InvalidOperationException("The plugin window factory returned null.");
        OpenWindows.Add(fullKey, window);
        window.Closed += (_, _) =>
        {
            if (OpenWindows.TryGetValue(fullKey, out var tracked) && ReferenceEquals(tracked, window))
                OpenWindows.Remove(fullKey);
        };

        try
        {
            window.Show();
            window.Activate();
        }
        catch
        {
            OpenWindows.Remove(fullKey);
            throw;
        }
    }

    private static void CloseWhere(Func<string, bool> predicate)
    {
        var matches = OpenWindows.Where(pair => predicate(pair.Key)).ToArray();
        foreach (var (_, window) in matches) window.Close();
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static string FullKey(string pluginId, string key) => pluginId + "\0" + key;
}

/// <summary>A plugin-specific handle used to open single-instance windows.</summary>
public sealed class PluginWindowScope
{
    private readonly string _pluginId;

    internal PluginWindowScope(string pluginId) => _pluginId = pluginId;

    /// <summary>
    /// Shows a window, or activates the already-open window with the same key. The factory always
    /// runs on the UI thread and is not called while that keyed window remains open.
    /// </summary>
    public void Show(string key, Func<Window> create) => PluginWindows.Show(_pluginId, key, create);

    /// <summary>Closes every currently open window belonging to this plugin.</summary>
    public void CloseAll() => PluginWindows.CloseFor(_pluginId);
}
