using PlugLauncher.Platform.Linux;
using PlugLauncher.Platform.MacOs;
using PlugLauncher.Platform.Windows;

namespace PlugLauncher.Platform;

/// <summary>
/// ثبت یک ترکیب کلید که هرجای سیستم زده شود می‌آید، حتی وقتی برنامه فوکوس ندارد.
///
/// این سخت‌ترین تکه‌ی پورت است و روی هر سیستم مکانیزم کاملاً متفاوتی دارد؛ روی Wayland اصلاً
/// چنین چیزی وجود ندارد. برای همین شکست، حالت عادی به حساب می‌آید نه استثنا: متد
/// <see cref="TryRegister"/> دروغ نمی‌گوید و <c>problem</c> جمله‌ای است که می‌شود به کاربر نشان داد.
/// </summary>
public interface IHotkeys : IDisposable
{
    /// <summary>
    /// باید روی تردی صدا زده شود که حلقه‌ی پیام دارد — یعنی همان ترد UI.
    /// <paramref name="pressed"/> هم روی همان ترد صدا زده می‌شود.
    /// </summary>
    bool TryRegister(Hotkey hotkey, Action pressed, out string problem);

    void Unregister();
}

/// <summary>اجرای خودکار موقع ورود کاربر به سیستم.</summary>
public interface IAutoStart
{
    /// <summary>آیا الان ثبت شده و به <b>همین</b> فایل اجرایی اشاره می‌کند.</summary>
    bool IsEnabled();

    bool Set(bool enabled, out string problem);
}

/// <summary>باز کردن چیزی با همان برنامه‌ای که سیستم برایش انتخاب می‌کند.</summary>
public interface IOpener
{
    /// <summary>یک فایل، پوشه یا آدرس.</summary>
    bool Open(string target, out string problem);

    /// <summary>نشان دادن یک فایل در فایل‌منیجر، انتخاب‌شده.</summary>
    bool Reveal(string path, out string problem);
}

/// <summary>
/// همان تکه‌هایی که روی هر سیستم‌عامل فرق می‌کنند، پشت یک در.
///
/// انتخاب در زمان اجرا انجام می‌شود نه با <c>#if</c>، چون یک بیلد باید هر سه جا اجرا شود.
/// </summary>
public static class Os
{
    /// <summary>نام این سیستم‌عامل، با همان املایی که در <c>plugin.json</c> نوشته می‌شود.</summary>
    public static string Name { get; } =
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : OperatingSystem.IsLinux() ? "linux"
        : "unknown";

    public static IAutoStart AutoStart { get; } =
        OperatingSystem.IsWindows() ? new WindowsAutoStart()
        : OperatingSystem.IsMacOS() ? new MacAutoStart()
        : OperatingSystem.IsLinux() ? new LinuxAutoStart()
        : new NoAutoStart();

    public static IOpener Opener { get; } =
        OperatingSystem.IsWindows() ? new WindowsOpener()
        : OperatingSystem.IsMacOS() ? new MacOpener()
        : OperatingSystem.IsLinux() ? new LinuxOpener()
        : new NoOpener();

    /// <summary>هربار یک نمونه‌ی تازه: ثبت هات‌کی منابع سیستمی می‌گیرد و باید Dispose شود.</summary>
    public static IHotkeys CreateHotkeys()
        => OperatingSystem.IsWindows() ? new WindowsHotkeys() : new NoHotkeys();
}

/// <summary>وقتی سیستم‌عامل شناخته نیست یا هنوز پیاده‌سازی ندارد. بی‌صدا شکست نمی‌خورد.</summary>
internal sealed class NoHotkeys : IHotkeys
{
    public bool TryRegister(Hotkey hotkey, Action pressed, out string problem)
    {
        problem = $"a system-wide hotkey is not supported on {Os.Name} yet";
        return false;
    }

    public void Unregister() { }
    public void Dispose() { }
}

internal sealed class NoAutoStart : IAutoStart
{
    public bool IsEnabled() => false;

    public bool Set(bool enabled, out string problem)
    {
        problem = $"starting with the system is not supported on {Os.Name}";
        return false;
    }
}

internal sealed class NoOpener : IOpener
{
    public bool Open(string target, out string problem)
    {
        problem = $"opening things is not supported on {Os.Name}";
        return false;
    }

    public bool Reveal(string path, out string problem) => Open(path, out problem);
}
