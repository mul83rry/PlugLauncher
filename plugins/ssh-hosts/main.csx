// SSH hosts from ~/.ssh/config. Keyword: ssh
//
//   ssh                every host in the config
//   ssh prod           filter by alias, hostname or user
//   ssh root@10.0.0.5  connect to something that is not in the config at all
//
// Enter opens a terminal running "ssh <alias>" — Windows Terminal or PowerShell on Windows,
// Terminal.app on macOS, and whichever terminal is installed on Linux. Wildcard blocks
// (Host *) are skipped: they are defaults for other hosts, not something you can connect to.

class SshHost
{
    public string Alias = "";
    public string HostName = "";
    public string User = "";
    public string Port = "";
    public string Identity = "";
    public string ProxyJump = "";

    // "root@1.2.3.4:2222 · key: id_ed25519 · via bastion"
    public string Describe()
    {
        var target = string.IsNullOrEmpty(HostName) ? Alias : HostName;
        if (!string.IsNullOrEmpty(User)) target = $"{User}@{target}";
        if (!string.IsNullOrEmpty(Port) && Port != "22") target += $":{Port}";

        var parts = new List<string> { target };
        if (!string.IsNullOrEmpty(Identity)) parts.Add($"key: {Path.GetFileName(Identity)}");
        if (!string.IsNullOrEmpty(ProxyJump)) parts.Add($"via {ProxyJump}");

        return string.Join("  ·  ", parts);
    }
}

string SshDir()
    => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

string ConfigPath() => Path.Combine(SshDir(), "config");

// ---------- config parsing ----------
// The file is reloaded whenever its timestamp moves, so editing the config does not need
// a restart. Includes are followed too, since plenty of setups split their hosts up.
List<SshHost>? cache;
DateTime cacheStamp;

DateTime StampOf(string path)
{
    try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
    catch { return DateTime.MinValue; }
}

// ~ and relative paths in an Include are both resolved against ~/.ssh
string Expand(string path)
{
    if (path.StartsWith("~"))
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path.TrimStart('~', '/', '\\'));
    }

    return Path.IsPathRooted(path) ? path : Path.Combine(SshDir(), path);
}

// "Key value", "Key=value" and "Key = value" are all legal in an ssh config
(string Key, string Value) SplitLine(string line)
{
    var text = line.Trim();
    var comment = text.IndexOf('#');
    if (comment >= 0) text = text.Substring(0, comment).Trim();
    if (text.Length == 0) return ("", "");

    var separator = text.IndexOfAny(new[] { ' ', '\t', '=' });
    if (separator < 0) return (text, "");

    return (text.Substring(0, separator).Trim(), text.Substring(separator + 1).Trim(' ', '\t', '=', '"'));
}

void ParseFile(string path, List<SshHost> into, int depth)
{
    if (depth > 4 || !File.Exists(path)) return;

    string[] lines;
    try { lines = File.ReadAllLines(path); }
    catch { return; }

    // every alias on a "Host a b c" line gets the settings that follow it
    var current = new List<SshHost>();

    foreach (var line in lines)
    {
        var (key, value) = SplitLine(line);
        if (key.Length == 0) continue;

        switch (key.ToLowerInvariant())
        {
            case "host":
                current.Clear();
                foreach (var alias in value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // patterns and negations are defaults for other hosts, not connectable targets
                    if (alias.Contains('*') || alias.Contains('?') || alias.StartsWith("!")) continue;

                    var host = new SshHost { Alias = alias };
                    current.Add(host);
                    into.Add(host);
                }
                break;

            case "include":
                foreach (var item in value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var expanded = Expand(item);
                    var directory = Path.GetDirectoryName(expanded);
                    var pattern = Path.GetFileName(expanded);
                    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;

                    string[] files;
                    try { files = Directory.GetFiles(directory, pattern); }
                    catch { continue; }

                    foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        ParseFile(file, into, depth + 1);
                }
                break;

            case "hostname": foreach (var h in current) h.HostName = value; break;
            case "user": foreach (var h in current) h.User = value; break;
            case "port": foreach (var h in current) h.Port = value; break;
            case "identityfile": foreach (var h in current) h.Identity = value; break;
            case "proxyjump": foreach (var h in current) h.ProxyJump = value; break;
        }
    }
}

List<SshHost> Hosts()
{
    var stamp = StampOf(ConfigPath());
    if (cache is not null && stamp == cacheStamp) return cache;

    var hosts = new List<SshHost>();
    ParseFile(ConfigPath(), hosts, 0);

    cacheStamp = stamp;
    cache = hosts.OrderBy(h => h.Alias, StringComparer.CurrentCultureIgnoreCase).ToList();
    return cache;
}

// ---------- connecting ----------
// The target ends up on a command line, so anything that could carry a second command
// (quotes, spaces, ; & |) is rejected rather than escaped.
bool IsSafeTarget(string target)
    => target.Length > 0
       && target.Length < 200
       && target.All(c => char.IsLetterOrDigit(c) || "._-@:%+/[]".Contains(c));

// Every system has ssh; none of them agrees on how to open a window to run it in. The three
// branches are the whole difference between the systems in this plugin.
void Connect(string target)
{
    if (!IsSafeTarget(target)) return;

    if (OperatingSystem.IsWindows()) { ConnectOnWindows(target); return; }
    if (OperatingSystem.IsMacOS()) { ConnectOnMac(target); return; }

    ConnectOnLinux(target);
}

void ConnectOnWindows(string target)
{
    var terminal = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "wt.exe");

    try
    {
        if (File.Exists(terminal))
        {
            Process.Start(new ProcessStartInfo(terminal, $"new-tab --title {target} ssh {target}")
            {
                UseShellExecute = true
            });
            return;
        }
    }
    catch
    {
        // wt.exe is a store alias and can fail to launch even when the file is there
    }

    Process.Start(new ProcessStartInfo("powershell.exe", $"-NoExit -Command ssh {target}")
    {
        UseShellExecute = true
    });
}

// "open -a Terminal" cannot be given a command to run, so Terminal is asked in its own
// language. The target has already been through IsSafeTarget, so there is nothing in it that
// AppleScript could read as a second statement.
void ConnectOnMac(string target)
{
    var start = new ProcessStartInfo("osascript");
    start.ArgumentList.Add("-e");
    start.ArgumentList.Add($"tell application \"Terminal\" to do script \"ssh {target}\"");
    start.ArgumentList.Add("-e");
    start.ArgumentList.Add("tell application \"Terminal\" to activate");

    try { Process.Start(start); } catch { }
}

// There is no such thing as "the terminal" on Linux. x-terminal-emulator is the one name a
// distribution is supposed to provide; the rest are what people actually have installed.
void ConnectOnLinux(string target)
{
    string[][] candidates =
    [
        ["x-terminal-emulator", "-e"],
        ["gnome-terminal", "--"],
        ["konsole", "-e"],
        ["xfce4-terminal", "-e"],
        ["alacritty", "-e"],
        ["kitty"],
        ["xterm", "-e"]
    ];

    foreach (var candidate in candidates)
    {
        var start = new ProcessStartInfo(candidate[0]);
        foreach (var argument in candidate.Skip(1)) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("ssh");
        start.ArgumentList.Add(target);

        // the name is looked up on PATH, so "not installed" arrives here as an exception
        try
        {
            Process.Start(start);
            return;
        }
        catch
        {
            // not installed — try the next one
        }
    }
}

// ---------- scoring ----------
// The alias is what people remember, so a hostname or user match is worth less.
int ScoreOf(SshHost host, string term)
{
    if (term.Length == 0) return 10;

    var fields = new[] { (host.Alias, 0), (host.HostName, 20), (host.User, 20) };
    var best = -1;

    foreach (var (field, penalty) in fields)
    {
        if (string.IsNullOrEmpty(field)) continue;

        if (field.Equals(term, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 100 - penalty);
        else if (field.StartsWith(term, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 80 - penalty);
        else if (field.Contains(term, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 50 - penalty);
    }

    return best;
}

// ---------- plugin ----------
return Plugin.Create(query =>
{
    var term = query.Search.Trim();
    var hosts = Hosts();
    var results = new List<PluginResult>();

    var matches = hosts
        .Select(h => new { host = h, score = ScoreOf(h, term) })
        .Where(x => x.score >= 0)
        .OrderByDescending(x => x.score)
        .ThenBy(x => x.host.Alias, StringComparer.CurrentCultureIgnoreCase)
        .Take(20);

    foreach (var match in matches)
    {
        var host = match.host;

        results.Add(new PluginResult
        {
            Id = host.Alias,
            Title = host.Alias,
            Subtitle = $"{host.Describe()}  ·  Enter to connect",
            Score = 100 + match.score,
            Action = () => Connect(host.Alias)
        });
    }

    // something typed that is not in the config — user@host or a bare address still works
    if (results.Count == 0 && IsSafeTarget(term) && (term.Contains('@') || term.Contains('.')))
    {
        results.Add(new PluginResult
        {
            Id = "adhoc",
            Title = $"ssh {term}",
            Subtitle = "Not in ~/.ssh/config  ·  Enter to connect anyway",
            Score = 150,
            Action = () => Connect(term)
        });
    }

    if (hosts.Count == 0 && term.Length == 0)
    {
        results.Add(new PluginResult
        {
            Id = "empty",
            Title = "No hosts in ~/.ssh/config",
            Subtitle = File.Exists(ConfigPath())
                ? "The file has no Host entries apart from wildcards"
                : $"{ConfigPath()} does not exist yet",
            Score = 100
        });
    }

    // low scores, so these never push a real host off the list
    if (File.Exists(ConfigPath()))
    {
        results.Add(new PluginResult
        {
            Id = "open-config",
            Title = "Open ~/.ssh/config",
            Subtitle = $"{ConfigPath()}  ·  {hosts.Count} host(s)",
            Score = 5,
            Action = () => Shell.Open(ConfigPath())
        });

        results.Add(new PluginResult
        {
            Id = "copy-config-path",
            Title = "Copy the config path",
            Subtitle = "Enter copies it to the clipboard",
            Score = 4,
            Action = () => Clipboard.Copy(ConfigPath())
        });
    }

    return results;
});
