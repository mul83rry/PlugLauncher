using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <summary>لاگ‌گیر فایلی ساده و thread-safe؛ همه‌ی پلاگین‌ها و خود میزبان در یک فایل می‌نویسند.</summary>
public sealed class FileLogger(string source) : IPluginLogger
{
    private static readonly Lock Gate = new();

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{source}] {message}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PluginPaths.LogFile)!);
                File.AppendAllText(PluginPaths.LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // لاگ نباید هیچ‌وقت باعث کرش شود
        }

        System.Diagnostics.Debug.WriteLine(line);
    }
}
