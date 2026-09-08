using System.Diagnostics;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxOpener : IOpener
{
    public bool Open(string target, out string problem) => Run.Command("xdg-open", out problem, target);

    /// <summary>
    /// لینوکس راه یکسانی برای «این فایل را انتخاب‌شده نشان بده» ندارد — هر فایل‌منیجر سوییچ
    /// خودش را دارد. پوشه‌ی دربرگیرنده باز می‌شود، که همه‌جا کار می‌کند.
    /// </summary>
    public bool Reveal(string path, out string problem) => Open(Run.Folder(path), out problem);
}
