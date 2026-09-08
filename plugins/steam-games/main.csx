// Sample plugin: finds installed Steam games and launches them on Enter.
// Keyword: st   (for example: "st raft")
//
// Steam keeps its library in the same shape everywhere — libraryfolders.vdf and one
// appmanifest per game — so only finding the Steam folder itself differs by system.

using System.Text.RegularExpressions;
using Microsoft.Win32;

// ---------- model ----------
class Game
{
    public string AppId = "";
    public string Name = "";
    public string InstallDir = "";
    public string? Icon;
}

List<Game>? cache = null;

// ---------- locate the Steam install ----------
string? FindSteamPath()
{
    if (OperatingSystem.IsWindows())
    {
        var fromRegistry = FindSteamInRegistry();
        if (fromRegistry is not null) return fromRegistry;
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    string[] candidates = OperatingSystem.IsMacOS()
        ? [Path.Combine(home, "Library", "Application Support", "Steam")]
        : OperatingSystem.IsLinux()
            ? [
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                // the flatpak build keeps its own home
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
              ]
            : [
                // the registry is gone or empty; these are where the installer puts it
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
              ];

    return candidates.FirstOrDefault(Directory.Exists);
}

// Guarded by the caller: the registry types exist everywhere, but answering is a Windows-only
// trick and everywhere else they throw.
string? FindSteamInRegistry()
{
    if (!OperatingSystem.IsWindows()) return null;

    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
    {
        using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
        var path = hkcu.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path.Replace('/', '\\');

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        var installPath = hklm.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("InstallPath") as string;
        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath)) return installPath;
    }

    return null;
}

// ---------- Steam library folders ----------
IEnumerable<string> LibraryFolders(string steamPath)
{
    yield return steamPath;

    var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
    if (!File.Exists(vdf)) yield break;

    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
    {
        // the file escapes its backslashes, which only Windows paths have
        var path = match.Groups[1].Value.Replace(@"\\", @"\");
        if (Directory.Exists(path)) yield return path;
    }
}

// ---------- read games from the appmanifest files ----------
List<Game> LoadGames()
{
    var games = new List<Game>();
    var steamPath = FindSteamPath();
    if (steamPath is null) return games;

    var skip = new[] { "Steamworks Common Redistributables", "Proton", "Steam Linux Runtime", "Steam Controller" };

    foreach (var library in LibraryFolders(steamPath).Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var steamapps = Path.Combine(library, "steamapps");
        if (!Directory.Exists(steamapps)) continue;

        foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
        {
            string text;
            try { text = File.ReadAllText(manifest); } catch { continue; }

            var appId = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
            var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
            var installDir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name)) continue;
            if (skip.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (games.Any(g => g.AppId == appId)) continue;

            games.Add(new Game
            {
                AppId = appId,
                Name = name,
                InstallDir = Path.Combine(steamapps, "common", installDir),
                Icon = FindIcon(steamPath, appId)
            });
        }
    }

    return games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}

string? FindIcon(string steamPath, string appId)
{
    var candidates = new[]
    {
        Path.Combine(steamPath, "appcache", "librarycache", appId, "icon.jpg"),
        Path.Combine(steamPath, "appcache", "librarycache", $"{appId}_icon.jpg"),
        Path.Combine(steamPath, "appcache", "librarycache", appId, "logo.png")
    };

    return candidates.FirstOrDefault(File.Exists);
}

// ---------- simple scoring ----------
int ScoreOf(string name, string term)
{
    if (string.IsNullOrEmpty(term)) return 10;
    if (name.Equals(term, StringComparison.CurrentCultureIgnoreCase)) return 100;
    if (name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) return 80;
    if (name.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 50;

    // initials match: "tab" -> "They Are Billions"
    var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]));
    return initials.StartsWith(term, StringComparison.CurrentCultureIgnoreCase) ? 60 : -1;
}

// ---------- plugin ----------
return Plugin.Create(query =>
{
    cache ??= LoadGames();

    var term = query.Search.Trim();

    return cache
        .Select(game => new { game, score = ScoreOf(game.Name, term) })
        .Where(x => x.score >= 0)
        .OrderByDescending(x => x.score)
        .Take(20)
        .Select(x => new PluginResult
        {
            Id = x.game.AppId,
            Title = x.game.Name,
            Subtitle = x.game.InstallDir,
            IconPath = x.game.Icon,
            Score = x.score,
            Action = () => Shell.Open($"steam://rungameid/{x.game.AppId}")
        });
});
