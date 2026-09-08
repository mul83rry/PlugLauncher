using System.Diagnostics;
using System.Runtime.Versioning;

namespace PlugLauncher.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsOpener : IOpener
{
    public bool Open(string target, out string problem)
        => Run.Start(new ProcessStartInfo(target) { UseShellExecute = true }, out problem);

    /// <summary>
    /// explorer باید <c>/select,PATH</c> را یک آرگومان ببیند، وگرنه به‌جای انتخاب کردن فایل،
    /// پوشه‌ی Documents را باز می‌کند. برای همین رشته‌ی خام و نه ArgumentList.
    /// </summary>
    public bool Reveal(string path, out string problem)
        => Run.Start(
            new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = true },
            out problem);
}
