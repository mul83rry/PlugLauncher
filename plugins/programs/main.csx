// پلاگین نمونه‌ی سراسری: شورت‌کات‌های منوی استارت را ایندکس می‌کند و با Enter اجرا می‌کند.
// چون کلیدواژه ندارد، هر چیزی که کاربر تایپ کند اینجا هم جستجو می‌شود.

class Shortcut
{
    public string Name = "";
    public string Path = "";
}

List<Shortcut>? cache = null;

List<Shortcut> LoadShortcuts()
{
    var roots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
    };

    var found = new Dictionary<string, Shortcut>(StringComparer.OrdinalIgnoreCase);

    foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
        catch { continue; }

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;

            found.TryAdd(name, new Shortcut { Name = name, Path = file });
        }
    }

    return found.Values.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}

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

    cache ??= LoadShortcuts();

    return cache
        .Select(s => new { s, score = ScoreOf(s.Name, term) })
        .Where(x => x.score >= 0)
        .OrderByDescending(x => x.score)
        .Take(15)
        .Select(x => new PluginResult
        {
            Id = x.s.Path,
            Title = x.s.Name,
            Subtitle = x.s.Path,
            IconPath = x.s.Path,
            Score = x.score,
            Action = () => Process.Start(new ProcessStartInfo(x.s.Path) { UseShellExecute = true })
        });
});
