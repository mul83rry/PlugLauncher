// Global sample plugin: finds the applications installed on a Mac and opens one on Enter.
// It has no keyword, so whatever the user types is searched here too — the macOS half of what
// "programs" does on Windows.
//
// The two stayed separate on purpose. A Mac has no Start menu and no .lnk files, so "programs"
// would have found nothing here and said nothing about why; and nothing but the scoring is
// shared between them, so one file with a platform switch would only have been longer.

class App
{
    public string Name = "";
    public string Path = "";
}

List<App>? cache = null;

// The three places a Mac keeps applications. Utilities, and the folders vendors make for
// themselves, sit inside these — the walk below goes into them.
string[] Roots()
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    return
    [
        "/Applications",
        "/System/Applications",
        Path.Combine(home, "Applications")
    ];
}

// A .app is a folder, so an ordinary recursive search walks *into* Safari.app and comes back
// with the helper bundles buried inside it. This stops at the first .app on every branch.
void Collect(string folder, int depth, Dictionary<string, App> found)
{
    IEnumerable<string> children;
    try { children = Directory.EnumerateDirectories(folder); }
    catch { return; }

    foreach (var child in children)
    {
        var name = Path.GetFileName(child);
        if (name.StartsWith('.')) continue;

        if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            var title = name[..^4];
            found.TryAdd(title, new App { Name = title, Path = child });
            continue;
        }

        if (depth > 0) Collect(child, depth - 1, found);
    }
}

List<App> LoadApps()
{
    var found = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);

    // two levels below a root reaches Utilities and the vendor folders, and no further
    foreach (var root in Roots().Where(Directory.Exists))
        Collect(root, 2, found);

    return found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}

// The same scoring as "programs", so the two feel like one feature on either system.
int ScoreOf(string name, string term)
{
    if (name.Equals(term, StringComparison.CurrentCultureIgnoreCase)) return 100;
    if (name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) return 75;
    if (name.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 45;

    var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]));
    return initials.StartsWith(term, StringComparison.CurrentCultureIgnoreCase) ? 55 : -1;
}

return Plugin.Create(query =>
{
    var term = query.Search.Trim();
    if (term.Length < 2) return [];

    cache ??= LoadApps();

    return cache
        .Select(a => new { a, score = ScoreOf(a.Name, term) })
        .Where(x => x.score >= 0)
        .OrderByDescending(x => x.score)
        .Take(15)
        .Select(x => new PluginResult
        {
            Id = x.a.Path,
            Title = x.a.Name,
            Subtitle = x.a.Path,
            // No IconPath on purpose. A Mac app keeps its icon as an .icns inside the bundle and
            // the launcher has no decoder for that format, so asking for it would fail on every
            // row. Left empty, each row shows this plugin's icon instead.
            Score = x.score,
            Action = () => Shell.Open(x.a.Path)
        });
});
