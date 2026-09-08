// What is taking the space. Keyword: du
//
//   du                       -> every drive, used share as a bar
//   du C:\                   -> the folders inside, biggest first          (Enter goes in)
//   du C:\ types             -> the same bytes grouped by kind of file      (Enter opens a kind)
//   du C:\ types Archives    -> the extensions inside that kind             (Enter opens one)
//   du C:\ ext .zip          -> the biggest files with that extension       (Enter shows it in Explorer)
//   du ~                     -> your profile folder
//
// The scan is one pass over the folder that keeps only sums: bytes per top-level child, per
// extension, and the eight biggest files of each extension. Nothing else is remembered, so a
// scan of a whole drive costs nothing in memory. It costs time — seconds for a drive — and the
// launcher gives a query three seconds, so a scan that is not done in time keeps running in the
// background and the row says so; Enter on it asks again.
//
// A finished scan is kept for five minutes. After that the old numbers are still shown while a
// fresh scan runs, rather than making you wait for a folder you have already seen.

using System.Collections.Concurrent;
using System.Diagnostics;

// ===== kinds of file =====

readonly Dictionary<string, string> Kinds = BuildKinds();

Dictionary<string, string> BuildKinds()
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    void Add(string kind, string extensions)
    {
        foreach (var e in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map["." + e] = kind;
    }

    Add("Video",     "mp4 mkv avi mov wmv flv webm m4v ts mpg mpeg");
    Add("Images",    "jpg jpeg png gif bmp webp heic heif tif tiff svg ico raw cr2 nef psd");
    Add("Audio",     "mp3 wav flac aac ogg m4a wma opus aiff");
    Add("Archives",  "zip rar 7z tar gz bz2 xz zst iso vhd vhdx img wim cab dmg");
    Add("Documents", "pdf doc docx xls xlsx ppt pptx txt md rtf odt ods odp csv epub mobi one");
    Add("Code",      "cs csx js ts tsx jsx py java kt cpp cc c h hpp go rs rb php swift json xml yaml yml toml html css scss sql sh ps1 bat cmd lua");
    Add("Programs",  "exe dll msi jar so pyd node lib pdb winmd");
    Add("System",    "sys drv efi log tmp dat bin db sqlite pak etl evtx mui cat");
    return map;
}

string KindOf(string extension)
    => extension.Length == 0 ? "No extension" : Kinds.GetValueOrDefault(extension, "Other");

string Ext(string extension) => extension.Length == 0 ? "(none)" : extension;

// ===== one scan =====

sealed class BigFile
{
    public string Path = "";
    public long Size;
}

sealed class Scan
{
    public required string Root;
    public Task Task = Task.CompletedTask;
    public Scan? Previous;

    public DateTime Started = DateTime.UtcNow;
    public DateTime Finished;
    public string? Error;

    // Live counters, read while the scan is running. Everything below them is written by the
    // scanning thread only and read only after Task has completed.
    public long FilesSoFar;
    public long BytesSoFar;

    public long Files;
    public long Bytes;
    public long LooseBytes;
    public int LooseFiles;

    public Dictionary<string, long> ByChild = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> ByExtension = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> CountByExtension = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<BigFile>> BiggestByExtension = new(StringComparer.OrdinalIgnoreCase);

    public bool Done => Task.IsCompleted;
    public bool Stale => Done && DateTime.UtcNow - Finished > TimeSpan.FromMinutes(5);
}

const int BiggestKept = 8;

// Each scan is tiny, but a laptop has more folders than anyone will ever look at in one sitting.
const int ScansKept = 32;

readonly ConcurrentDictionary<string, Scan> Scans = new(StringComparer.OrdinalIgnoreCase);

readonly EnumerationOptions Walk = new()
{
    RecurseSubdirectories = true,
    IgnoreInaccessible = true,
    // The default also skips Hidden and System, which would drop AppData — the biggest thing in
    // most profiles. Reparse points are the one thing that must be skipped: a junction or a
    // OneDrive placeholder would be counted twice, or forever.
    AttributesToSkip = FileAttributes.ReparsePoint
};

void Run(Scan scan)
{
    var root = scan.Root.EndsWith(Path.DirectorySeparatorChar) ? scan.Root : scan.Root + Path.DirectorySeparatorChar;
    var rootLength = root.Length;

    try
    {
        foreach (var file in new DirectoryInfo(scan.Root).EnumerateFiles("*", Walk))
        {
            long size;
            try { size = file.Length; } catch { continue; }

            scan.Files++;
            scan.Bytes += size;
            Interlocked.Exchange(ref scan.FilesSoFar, scan.Files);
            Interlocked.Exchange(ref scan.BytesSoFar, scan.Bytes);

            var full = file.FullName;
            var cut = full.IndexOf(Path.DirectorySeparatorChar, rootLength);
            if (cut < 0)
            {
                scan.LooseBytes += size;
                scan.LooseFiles++;
            }
            else
            {
                var child = full[rootLength..cut];
                scan.ByChild[child] = scan.ByChild.GetValueOrDefault(child) + size;
            }

            var extension = file.Extension;
            scan.ByExtension[extension] = scan.ByExtension.GetValueOrDefault(extension) + size;
            scan.CountByExtension[extension] = scan.CountByExtension.GetValueOrDefault(extension) + 1;

            if (!scan.BiggestByExtension.TryGetValue(extension, out var biggest))
                scan.BiggestByExtension[extension] = biggest = new List<BigFile>(BiggestKept + 1);

            if (biggest.Count < BiggestKept || size > biggest[^1].Size)
            {
                var at = biggest.FindIndex(b => size > b.Size);
                biggest.Insert(at < 0 ? biggest.Count : at, new BigFile { Path = full, Size = size });
                if (biggest.Count > BiggestKept) biggest.RemoveAt(biggest.Count - 1);
            }
        }
    }
    catch (Exception ex)
    {
        scan.Error = ex.Message;
    }

    scan.Finished = DateTime.UtcNow;
}

Scan GetOrStart(string root)
{
    if (Scans.TryGetValue(root, out var existing) && !existing.Stale) return existing;

    var fresh = new Scan { Root = root, Previous = existing };
    fresh.Task = Task.Run(() => Run(fresh));

    Scans[root] = fresh;
    Evict();
    return fresh;
}

void Evict()
{
    if (Scans.Count <= ScansKept) return;

    foreach (var old in Scans.Values.Where(s => s.Done).OrderBy(s => s.Finished).Take(Scans.Count - ScansKept).ToList())
        Scans.TryRemove(old.Root, out _);
}

// ===== formatting =====

string Human(long bytes)
{
    const double k = 1024;
    if (bytes < k) return $"{bytes} B";
    if (bytes < k * k) return $"{bytes / k:0} KB";
    if (bytes < k * k * k) return $"{bytes / k / k:0} MB";
    if (bytes < k * k * k * k) return $"{bytes / k / k / k:0.0} GB";
    return $"{bytes / k / k / k / k:0.00} TB";
}

string Percent(long part, long whole) => whole <= 0 ? "0%" : $"{100.0 * part / whole:0}%";
double Share(long part, long whole) => whole <= 0 ? 0 : Math.Clamp((double)part / whole, 0, 1);
string Count(long n, string noun) => $"{n:N0} {noun}{(n == 1 ? "" : "s")}";

// ===== actions =====

void OpenFolder(string path)
{
    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
}

void RevealFile(string path)
{
    try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
}

// ===== query parsing =====

sealed class Request
{
    public string Path = "";
    public string View = "folders";   // folders | types | kind | ext
    public string Argument = "";
}

/// <summary>
/// The verbs sit at the end so the path can contain spaces: everything before "types" or "ext"
/// is the path.
/// </summary>
Request Parse(string search)
{
    var text = search.Trim().Trim('"');
    var request = new Request();

    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (var i = words.Length - 1; i >= 0; i--)
    {
        if (words[i].Equals("types", StringComparison.OrdinalIgnoreCase))
        {
            var rest = words.Skip(i + 1).ToArray();
            request.View = rest.Length == 0 ? "types" : "kind";
            request.Argument = string.Join(' ', rest);
            text = string.Join(' ', words.Take(i));
            break;
        }

        if (words[i].Equals("ext", StringComparison.OrdinalIgnoreCase) && i < words.Length - 1)
        {
            request.View = "ext";
            var ext = words[i + 1];
            request.Argument = ext.StartsWith('.') || ext.Equals("none", StringComparison.OrdinalIgnoreCase) ? ext : "." + ext;
            text = string.Join(' ', words.Take(i));
            break;
        }
    }

    text = text.Trim();
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (text == "~") text = home;
    else if (text.StartsWith("~\\") || text.StartsWith("~/")) text = Path.Combine(home, text[2..]);

    // "C:" alone means the current directory of that drive to Windows; the user means the root.
    if (text.Length == 2 && text[1] == ':') text += "\\";

    request.Path = text;
    return request;
}

// ===== rows =====

PluginResult Note(string title, string subtitle, int score = 100) => new()
{
    Id = "note:" + title,
    Title = title,
    Subtitle = subtitle,
    Score = score
};

/// <summary>A row that re-runs the query with new text and keeps the window open.</summary>
PluginResult Drill(string id, string title, string subtitle, string next, double? fraction, int score) => new()
{
    Id = id,
    Title = title,
    Subtitle = subtitle,
    ReplaceQuery = next,
    Fraction = fraction,
    Score = score
};

List<PluginResult> Drives(string keyword, int scoreFrom = 200)
{
    var rows = new List<PluginResult>();
    var score = scoreFrom;

    foreach (var drive in DriveInfo.GetDrives())
    {
        long total, free;
        string label;
        try
        {
            if (!drive.IsReady) continue;
            total = drive.TotalSize;
            free = drive.TotalFreeSpace;
            label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel;
        }
        catch { continue; }

        var used = total - free;

        rows.Add(Drill(
            "drive:" + drive.Name,
            $"{drive.Name}  {label}",
            $"{Human(used)} used of {Human(total)}  ·  {Human(free)} free  ·  {Percent(used, total)}",
            $"{keyword} {drive.Name}",
            Share(used, total),
            score--));
    }

    if (rows.Count == 0) rows.Add(Note("No drives are ready", ""));
    return rows;
}

PluginResult Header(Scan scan, string title, string detail) => new()
{
    Id = "header:" + scan.Root,
    Title = title,
    Subtitle = $"{Human(scan.Bytes)}  ·  {Count(scan.Files, "file")}  ·  {detail}  ·  Enter opens in Explorer",
    Score = 300,
    Action = () => OpenFolder(scan.Root)
};

List<PluginResult> Folders(Scan scan, string keyword)
{
    var rows = new List<PluginResult> { Header(scan, scan.Root, "by folder") };

    var kinds = scan.ByExtension
        .GroupBy(kv => KindOf(kv.Key))
        .Select(g => (Kind: g.Key, Bytes: g.Sum(kv => kv.Value)))
        .OrderByDescending(x => x.Bytes)
        .Take(3)
        .Select(x => $"{x.Kind} {Human(x.Bytes)}");

    rows.Add(Drill("types:" + scan.Root, "By file type", string.Join("  ·  ", kinds),
                   $"{keyword} {scan.Root} types", null, 290));

    var score = 280;
    foreach (var (child, bytes) in scan.ByChild.OrderByDescending(kv => kv.Value))
    {
        var path = Path.Combine(scan.Root, child);
        rows.Add(Drill("dir:" + path, child, $"{Human(bytes)}  ·  {Percent(bytes, scan.Bytes)}",
                       $"{keyword} {path}", Share(bytes, scan.Bytes), score--));
    }

    if (scan.LooseFiles > 0)
    {
        rows.Add(new PluginResult
        {
            Id = "loose:" + scan.Root,
            Title = "Files directly in this folder",
            Subtitle = $"{Human(scan.LooseBytes)}  ·  {Count(scan.LooseFiles, "file")}  ·  {Percent(scan.LooseBytes, scan.Bytes)}",
            Fraction = Share(scan.LooseBytes, scan.Bytes),
            Score = score,
            Action = () => OpenFolder(scan.Root)
        });
    }

    return rows;
}

List<PluginResult> Types(Scan scan, string keyword)
{
    var rows = new List<PluginResult> { Header(scan, scan.Root, "by file type") };
    var score = 280;

    var kinds = scan.ByExtension
        .GroupBy(kv => KindOf(kv.Key))
        .Select(g => (
            Kind: g.Key,
            Bytes: g.Sum(kv => kv.Value),
            Top: g.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{Ext(kv.Key)} {Human(kv.Value)}")))
        .OrderByDescending(x => x.Bytes);

    foreach (var kind in kinds)
    {
        rows.Add(Drill("kind:" + scan.Root + ":" + kind.Kind, kind.Kind,
                       $"{Human(kind.Bytes)}  ·  {Percent(kind.Bytes, scan.Bytes)}  ·  {string.Join("  ", kind.Top)}",
                       $"{keyword} {scan.Root} types {kind.Kind}", Share(kind.Bytes, scan.Bytes), score--));
    }

    return rows;
}

List<PluginResult> Kind(Scan scan, string keyword, string kind)
{
    var rows = new List<PluginResult> { Header(scan, $"{scan.Root}  ·  {kind}", "by extension") };
    var score = 280;
    var any = false;

    foreach (var (extension, bytes) in scan.ByExtension
                 .Where(kv => KindOf(kv.Key).Equals(kind, StringComparison.OrdinalIgnoreCase))
                 .OrderByDescending(kv => kv.Value))
    {
        any = true;
        var count = scan.CountByExtension.GetValueOrDefault(extension);
        rows.Add(Drill("ext:" + scan.Root + ":" + extension, Ext(extension),
                       $"{Human(bytes)}  ·  {Count(count, "file")}  ·  {Percent(bytes, scan.Bytes)}",
                       $"{keyword} {scan.Root} ext {(extension.Length == 0 ? "none" : extension)}",
                       Share(bytes, scan.Bytes), score--));
    }

    if (!any)
        rows.Add(Note($"Nothing of kind \"{kind}\" here",
                      "Kinds: " + string.Join(", ", Kinds.Values.Distinct().Order()), 90));

    return rows;
}

List<PluginResult> Extension(Scan scan, string extension)
{
    var key = extension.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : extension;
    var rows = new List<PluginResult> { Header(scan, $"{scan.Root}  ·  {Ext(key)}", "biggest files") };
    var score = 280;

    if (!scan.BiggestByExtension.TryGetValue(key, out var biggest))
    {
        rows.Add(Note($"No {Ext(key)} files here", "", 90));
        return rows;
    }

    foreach (var file in biggest)
    {
        var folder = Path.GetDirectoryName(file.Path) ?? "";
        var inside = folder.StartsWith(scan.Root, StringComparison.OrdinalIgnoreCase) && folder.Length > scan.Root.Length
            ? folder[scan.Root.Length..].TrimStart('\\') + "\\"
            : "here";

        rows.Add(new PluginResult
        {
            Id = "file:" + file.Path,
            Title = Path.GetFileName(file.Path),
            Subtitle = $"{Human(file.Size)}  ·  {Percent(file.Size, scan.Bytes)}  ·  {inside}  ·  Enter shows it in Explorer",
            IconPath = file.Path,
            Fraction = Share(file.Size, scan.Bytes),
            Score = score--,
            Action = () => RevealFile(file.Path)
        });
    }

    return rows;
}

PluginResult Progress(Scan scan, PluginQuery query, string title)
{
    var files = Interlocked.Read(ref scan.FilesSoFar);
    var bytes = Interlocked.Read(ref scan.BytesSoFar);
    var seconds = (DateTime.UtcNow - scan.Started).TotalSeconds;

    // The same text again would not count as a change, so a trailing space is toggled.
    var again = query.Raw.EndsWith(' ') ? query.Raw.TrimEnd() : query.Raw + " ";

    return new PluginResult
    {
        Id = "scanning:" + scan.Root,
        Title = title,
        Subtitle = $"{Count(files, "file")}, {Human(bytes)} so far  ·  {seconds:0}s  ·  Enter asks again",
        ReplaceQuery = again,
        Score = 310
    };
}

// ===== entry =====

return Plugin.Create(async (query, cancellationToken) =>
{
    var keyword = query.Keyword.Length > 0 ? query.Keyword : "du";

    if (query.IsEmpty) return Drives(keyword);

    var request = Parse(query.Search);

    string root;
    try { root = Path.GetFullPath(request.Path); }
    catch { return [Note("That is not a path", request.Path)]; }

    if (!Directory.Exists(root))
        return [Note("Folder not found", root, 200), .. Drives(keyword, 100)];

    // A drive root keeps its trailing slash; anything else loses it, so "C:\Users\" and
    // "C:\Users" are one scan and one cache entry.
    root = root.Length > 3 ? root.TrimEnd('\\') : root;

    var scan = GetOrStart(root);

    // Most folders finish inside this. A drive does not, and then the row says so.
    if (!scan.Done) await Task.WhenAny(scan.Task, Task.Delay(2000, cancellationToken));

    var usable = scan.Done ? scan : scan.Previous?.Done == true ? scan.Previous : null;

    if (usable is null)
        return [Progress(scan, query, $"Scanning {scan.Root} \u2026")];

    if (usable.Error is not null && usable.Files == 0)
        return [Note("Could not read that folder", usable.Error)];

    List<PluginResult> rows = request.View switch
    {
        "types" => Types(usable, keyword),
        "kind" => Kind(usable, keyword, request.Argument),
        "ext" => Extension(usable, request.Argument),
        _ => Folders(usable, keyword)
    };

    // Old numbers while a fresh scan runs: shown, but said.
    if (usable != scan)
    {
        var age = (DateTime.UtcNow - usable.Finished).TotalMinutes;
        rows.Insert(0, Progress(scan, query, $"Refreshing\u2026 these numbers are {age:0} min old"));
    }

    return rows;
});
