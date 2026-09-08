using System.Diagnostics;

namespace PlugLauncher.Platform;

/// <summary>مشترکِ هر سه پیاده‌سازی: اجرای یک دستور و برگرداندن دلیل، نه پرت کردن استثنا.</summary>
internal static class Run
{
    public static bool Start(ProcessStartInfo info, out string problem)
    {
        try
        {
            Process.Start(info);
            problem = "";
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    public static bool Command(string file, out string problem, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        return Start(info, out problem);
    }

    /// <summary>پوشه‌ی دربرگیرنده‌ی یک مسیر، برای وقتی که فایل‌منیجر «انتخاب کردن» بلد نیست.</summary>
    public static string Folder(string path)
        => Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
}
