using System.Runtime.Versioning;

namespace PlugLauncher.Platform.MacOs;

[SupportedOSPlatform("macos")]
internal sealed class MacOpener : IOpener
{
    public bool Open(string target, out string problem) => Run.Command("open", out problem, target);

    public bool Reveal(string path, out string problem) => Run.Command("open", out problem, "-R", path);
}
