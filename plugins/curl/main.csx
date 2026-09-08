// Send a request and read what came back. Keyword: curl
//
//   curl                                 -> the requests you have sent, newest first
//   curl https://api.github.com/users/x  -> what would be sent            (Enter sends it)
//   curl -X POST https://x -d a=1        -> a body makes it a POST
//   curl #a3f                            -> the status and the fields of the answer
//   curl #a3f login                      -> only the fields whose path or value contains that
//   curl #a3f headers                    -> the response headers
//   curl #a3f body                       -> the raw body, in one row
//   curl #a3f again                      -> send the same command again
//   curl clear                           -> forget everything cached
//
// The short id exists because a curl command line runs to the end of the box: there is no room
// after it for a filter or a verb. The id is a hash of the command, so the same command always
// has the same id and yesterday's request is still reachable today.
//
// curl.exe is not called. The command line is parsed here and the request goes out through
// HttpClient, which is what makes it possible to show the answer field by field instead of as a
// wall of text -- and it keeps flags that write to disk out of the picture entirely. Supported:
// -X -H -d --data --data-raw --data-binary --json -u -A -b -e -L -I -G --url, plus the usual
// noise flags that get copied along (-s -i -v --compressed and friends). Anything else is
// reported rather than dropped, because a flag that disappears in silence changes what you
// think you sent.
//
// Nothing goes out while you type. Only "#id" sends, and that text arrives by pressing Enter.
// Two ceilings on top of that: half a second between two starts, and twenty requests a minute.
//
// What is kept on disk: the command lines and how each one last went -- not the response
// bodies, because a body is usually where the secrets are. Bodies stay in memory until the
// launcher closes. Note that a token you put in a command line is part of that command line.
// "curl clear" wipes both.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

// ===== what the host gives us =====

string DataDir = "";
string HistoryFile = "";
IPluginLogger? Log = null;

// ===== the wire =====

const int BodyLimit = 256 * 1024;
const int HistoryLimit = 30;
const int MinGapMs = 500;
const int PerMinute = 20;

readonly HttpClient Direct = NewClient(false);
readonly HttpClient Redirecting = NewClient(true);

HttpClient NewClient(bool follow) => new HttpClient(new HttpClientHandler
{
    AllowAutoRedirect = follow,
    MaxAutomaticRedirections = 10,
    UseCookies = false,
    AutomaticDecompression = DecompressionMethods.All
})
{
    Timeout = TimeSpan.FromSeconds(30)
};

// ===== a parsed command line =====

sealed class Req
{
    public string Method = "";
    public string Url = "";
    public List<KeyValuePair<string, string>> Headers = new();
    public string? Body;
    public string? ContentType;
    public bool Follow;
    public string? Error;
    public List<string> Unknown = new();
}

// ===== one run of one command =====

sealed class Run
{
    public string Id = "";
    public string Command = "";
    public Req Request = new();
    public DateTime Started;
    public DateTime Finished;
    public Task Task = Task.CompletedTask;
    public volatile bool Done;
    public int Status;
    public string Reason = "";
    public double Ms;
    public long Bytes;
    public string ContentType = "";
    public List<KeyValuePair<string, string>> Headers = new();
    public string Body = "";
    public bool BodyIsText;
    public bool Truncated;
    public string? Error;
}

// ===== what is remembered =====

sealed class Entry
{
    public string Id = "";
    public string Command = "";
    public string Method = "";
    public string Url = "";
    public int Status;
    public double Ms;
    public long Bytes;
    public DateTime When;
    public string Error = "";
}

readonly ConcurrentDictionary<string, Run> Runs = new(StringComparer.Ordinal);
readonly Dictionary<string, string> KnownCommands = new(StringComparer.Ordinal);
readonly List<Entry> Entries = new();
readonly object Gate = new();
readonly Queue<DateTime> Starts = new();
DateTime LastStart = DateTime.MinValue;

// ===== small helpers =====

string Human(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";

    string[] units = ["KB", "MB", "GB"];
    double value = bytes / 1024.0;
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }

    return $"{value:0.#} {units[unit]}";
}

string Took(double ms) => ms >= 1000 ? $"{ms / 1000:0.##} s" : $"{ms:0} ms";

string Ago(DateTime utc)
{
    var span = DateTime.UtcNow - utc;
    if (span.TotalSeconds < 60) return "just now";
    if (span.TotalMinutes < 60) return $"{span.TotalMinutes:0} min ago";
    if (span.TotalHours < 24) return $"{span.TotalHours:0} h ago";
    return $"{span.TotalDays:0} d ago";
}

string Cut(string text, int max)
{
    var flat = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
    flat = flat.Trim();

    return flat.Length > max ? flat[..(max - 1)] + "…" : flat;
}

void Copy(string value)
{
    // the WPF clipboard occasionally loses the race with whatever owned it last
    for (var attempt = 0; attempt < 3; attempt++)
    {
        try { System.Windows.Clipboard.SetText(value); return; }
        catch { Thread.Sleep(40); }
    }
}

PluginResult Note(string title, string subtitle = "", int score = 500)
    => new PluginResult { Id = "note:" + title, Title = title, Subtitle = subtitle, Score = score };

PluginResult Copyable(string id, string title, string subtitle, int score)
    => new PluginResult { Id = id, Title = title, Subtitle = subtitle, Score = score, Action = () => Copy(title) };

PluginResult Go(string id, string title, string subtitle, string command, int score)
    => new PluginResult { Id = id, Title = title, Subtitle = subtitle, Score = score, ReplaceQuery = command };

// ===== reading a curl command line =====

/// <summary>
/// Splits on spaces the way a shell would, so a quoted header stays one token. A backslash at
/// the end of a line is what is left of the line continuations in a copied multi-line command,
/// and it is dropped rather than becoming part of a value.
/// </summary>
List<string> Tokenize(string text)
{
    var tokens = new List<string>();
    var current = new StringBuilder();
    var quote = '\0';
    var quoted = false;

    void Flush()
    {
        if (quoted || current.Length > 0) tokens.Add(current.ToString());
        current.Clear();
        quoted = false;
    }

    for (var i = 0; i < text.Length; i++)
    {
        var c = text[i];

        if (quote != '\0')
        {
            if (c == quote) quote = '\0';
            else current.Append(c);
            continue;
        }

        if (c == '\'' || c == '"')
        {
            quote = c;
            quoted = true;
        }
        else if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
        {
            Flush();
        }
        else if (c == '\\' && (i == text.Length - 1 || text[i + 1] is ' ' or '\t' or '\r' or '\n' or '-'))
        {
            // The end of a line in a copied multi-line command. Pasting one into a single-line
            // box can drop the newline and leave the backslash against the next flag, so
            // "\--header" is the same thing and has to end the token too.
            Flush();
        }
        else
        {
            current.Append(c);
        }
    }

    Flush();
    return tokens;
}

/// <summary>
/// The command as it is stored and hashed: one line, no leading "curl", no double spaces. A
/// pasted multi-line command has to come down to one line here, or the same command would hash
/// to a different id depending on how it arrived, and a history row would not fit on one line.
/// </summary>
string Normalize(string command)
{
    var text = command.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
    while (text.Contains("  ")) text = text.Replace("  ", " ");
    if (text.StartsWith("curl ", StringComparison.OrdinalIgnoreCase)) text = text[5..].Trim();

    return text;
}

string ShortId(string command)
    => "#" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command)))[..4].ToLowerInvariant();

void AddHeader(Req req, string raw)
{
    var cut = raw.IndexOf(':');
    if (cut <= 0) { req.Unknown.Add($"-H {raw}"); return; }

    req.Headers.Add(new KeyValuePair<string, string>(raw[..cut].Trim(), raw[(cut + 1)..].Trim()));
}

Req Parse(string command)
{
    var req = new Req();
    var tokens = Tokenize(command);

    for (var i = 0; i < tokens.Count; i++)
    {
        var token = tokens[i];
        string Next() => i + 1 < tokens.Count ? tokens[++i] : "";

        switch (token)
        {
            case "-X": case "--request": req.Method = Next().ToUpperInvariant(); break;
            case "-H": case "--header": AddHeader(req, Next()); break;

            case "-d": case "--data": case "--data-raw": case "--data-ascii": case "--data-binary":
                req.Body = Next();
                break;

            case "--json":
                req.Body = Next();
                req.ContentType = "application/json";
                req.Headers.Add(new KeyValuePair<string, string>("Accept", "application/json"));
                break;

            case "-u": case "--user":
                req.Headers.Add(new KeyValuePair<string, string>(
                    "Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Next()))));
                break;

            case "-A": case "--user-agent": req.Headers.Add(new KeyValuePair<string, string>("User-Agent", Next())); break;
            case "-b": case "--cookie": req.Headers.Add(new KeyValuePair<string, string>("Cookie", Next())); break;
            case "-e": case "--referer": req.Headers.Add(new KeyValuePair<string, string>("Referer", Next())); break;
            case "-L": case "--location": req.Follow = true; break;
            case "-I": case "--head": req.Method = "HEAD"; break;
            case "-G": case "--get": req.Method = "GET"; break;
            case "--url": req.Url = Next(); break;

            // copied commands carry these along and they change nothing here
            case "-s": case "--silent": case "-S": case "--show-error": case "-i": case "--include":
            case "-v": case "--verbose": case "--compressed": case "-#": case "--progress-bar":
            case "--no-progress-meter": case "-f": case "--fail":
                break;

            default:
                if (token.StartsWith('-')) req.Unknown.Add(token);
                else if (req.Url.Length == 0) req.Url = token;
                else req.Unknown.Add(token);
                break;
        }
    }

    if (req.Url.Length == 0)
    {
        req.Error = "there is no URL in that command";
        return req;
    }

    if (!req.Url.Contains("://")) req.Url = "https://" + req.Url;

    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
    {
        req.Error = $"\"{req.Url}\" is not an http or https URL";
        return req;
    }

    req.Url = uri.ToString();
    if (req.Method.Length == 0) req.Method = req.Body is null ? "GET" : "POST";

    // curl's own default for -d, and the one people forget they are relying on
    if (req.Body is not null && req.ContentType is null)
    {
        var given = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        req.ContentType = given.Value ?? "application/x-www-form-urlencoded";
    }

    return req;
}

// ===== sending =====

bool IsText(string contentType)
{
    if (contentType.Length == 0) return true;

    var lower = contentType.ToLowerInvariant();
    return lower.StartsWith("text/")
           || lower.Contains("json") || lower.Contains("xml") || lower.Contains("javascript")
           || lower.Contains("x-www-form-urlencoded") || lower.Contains("csv");
}

string Reason(Exception ex)
{
    var inner = ex;
    while (inner.InnerException is not null) inner = inner.InnerException;

    return ex is TaskCanceledException ? "no answer within 30 seconds" : inner.Message;
}

async Task Execute(Run run)
{
    var watch = Stopwatch.StartNew();

    try
    {
        using var message = new HttpRequestMessage(new HttpMethod(run.Request.Method), run.Request.Url);

        if (run.Request.Body is not null)
        {
            message.Content = new StringContent(run.Request.Body, Encoding.UTF8);
            message.Content.Headers.Remove("Content-Type");
            if (!message.Content.Headers.TryAddWithoutValidation("Content-Type", run.Request.ContentType))
                message.Content.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        }

        foreach (var header in run.Request.Headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var client = run.Request.Follow ? Redirecting : Direct;
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        run.Status = (int)response.StatusCode;
        run.Reason = response.ReasonPhrase ?? response.StatusCode.ToString();
        run.Bytes = bytes.LongLength;
        run.ContentType = response.Content.Headers.ContentType?.ToString() ?? "";
        run.Headers = response.Headers
            .Concat(response.Content.Headers)
            .Select(h => new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)))
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        run.BodyIsText = IsText(run.ContentType);
        if (run.BodyIsText)
        {
            run.Truncated = bytes.Length > BodyLimit;
            run.Body = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, BodyLimit));
        }
    }
    catch (Exception ex)
    {
        run.Error = Reason(ex);
        Log?.Warn($"{run.Request.Method} {run.Request.Url} failed: {run.Error}");
    }
    finally
    {
        run.Ms = watch.Elapsed.TotalMilliseconds;
        run.Finished = DateTime.UtcNow;
        run.Done = true;
        Remember(run);
    }
}

/// <summary>
/// The two ceilings. Typing cannot get here -- only "#id" sends, and that text arrives by
/// pressing Enter -- but an id typed by hand is still one query per keystroke, and a request
/// that leaves the machine is not something to be casual about. An empty string means "wait a
/// moment"; a sentence means "no".
/// </summary>
string? TooSoon()
{
    lock (Gate)
    {
        var now = DateTime.UtcNow;
        while (Starts.Count > 0 && (now - Starts.Peek()).TotalMinutes >= 1) Starts.Dequeue();

        if (Starts.Count >= PerMinute) return $"{PerMinute} requests went out in the last minute";
        if ((now - LastStart).TotalMilliseconds < MinGapMs) return "";

        return null;
    }
}

Run Start(string id, string command)
{
    var run = new Run { Id = id, Command = command, Request = Parse(command), Started = DateTime.UtcNow };

    lock (Gate)
    {
        LastStart = run.Started;
        Starts.Enqueue(run.Started);
    }

    Runs[id] = run;
    run.Task = Task.Run(() => Execute(run));

    return run;
}

// ===== what is remembered =====

string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

void Load()
{
    try
    {
        if (!File.Exists(HistoryFile)) return;

        foreach (var line in File.ReadAllLines(HistoryFile))
        {
            var parts = line.Split('\t');
            if (parts.Length < 7) continue;

            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                continue;

            var command = parts[6];
            var entry = new Entry
            {
                Id = ShortId(command),
                Command = command,
                When = when,
                Status = int.TryParse(parts[1], out var status) ? status : 0,
                Ms = double.TryParse(parts[2], CultureInfo.InvariantCulture, out var ms) ? ms : 0,
                Bytes = long.TryParse(parts[3], out var bytes) ? bytes : 0,
                Method = parts[4],
                Error = parts[5],
                Url = Parse(command).Url
            };

            Entries.Add(entry);
            KnownCommands[entry.Id] = command;
        }
    }
    catch (Exception ex)
    {
        Log?.Warn("could not read the history: " + ex.Message);
    }
}

void Save()
{
    try
    {
        Directory.CreateDirectory(DataDir);

        File.WriteAllLines(HistoryFile, Entries
            .Take(HistoryLimit)
            .Select(e => string.Join('\t',
                e.When.ToString("o", CultureInfo.InvariantCulture),
                e.Status.ToString(CultureInfo.InvariantCulture),
                ((long)e.Ms).ToString(CultureInfo.InvariantCulture),
                e.Bytes.ToString(CultureInfo.InvariantCulture),
                Clean(e.Method),
                Clean(e.Error),
                Clean(e.Command))));
    }
    catch (Exception ex)
    {
        Log?.Warn("could not write the history: " + ex.Message);
    }
}

void Remember(Run run)
{
    lock (Gate)
    {
        Entries.RemoveAll(e => e.Id == run.Id);
        Entries.Insert(0, new Entry
        {
            Id = run.Id,
            Command = run.Command,
            Method = run.Request.Method,
            Url = run.Request.Url,
            Status = run.Status,
            Ms = run.Ms,
            Bytes = run.Bytes,
            When = run.Finished,
            Error = run.Error ?? ""
        });

        if (Entries.Count > HistoryLimit) Entries.RemoveRange(HistoryLimit, Entries.Count - HistoryLimit);
        KnownCommands[run.Id] = run.Command;
        Save();
    }
}

// ===== reading the answer =====

/// <summary>Every leaf of a JSON document, as the path to it and the value at it.</summary>
void Flatten(JsonElement element, string path, List<KeyValuePair<string, string>> into, int depth)
{
    if (into.Count >= 300 || depth > 8) return;

    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            var empty = true;
            foreach (var property in element.EnumerateObject())
            {
                empty = false;
                Flatten(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, into, depth + 1);
            }
            if (empty && path.Length > 0) into.Add(new KeyValuePair<string, string>(path, "{}"));
            break;

        case JsonValueKind.Array:
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Flatten(item, $"{path}[{index}]", into, depth + 1);
                if (++index >= 100) break;
            }
            if (index == 0) into.Add(new KeyValuePair<string, string>(path, "[]"));
            break;

        default:
            into.Add(new KeyValuePair<string, string>(path, element.ToString()));
            break;
    }
}

List<KeyValuePair<string, string>>? Fields(Run run)
{
    if (!run.BodyIsText || run.Truncated || run.Body.Trim().Length == 0) return null;

    try
    {
        using var document = JsonDocument.Parse(run.Body, new JsonDocumentOptions { AllowTrailingCommas = true });
        var fields = new List<KeyValuePair<string, string>>();
        Flatten(document.RootElement, "", fields, 0);

        return fields.Count == 0 ? null : fields;
    }
    catch (JsonException)
    {
        return null;
    }
}

// ===== views =====

readonly string[] Verbs = ["headers", "body", "again"];

PluginResult Progress(Run run)
    => new PluginResult
    {
        Id = "waiting:" + run.Id,
        Title = $"{run.Request.Method} {Cut(run.Request.Url, 70)} …",
        Subtitle = $"waiting for an answer  ·  {(DateTime.UtcNow - run.Started).TotalSeconds:0.0}s",
        RefreshAfterMs = 300,
        Score = 500
    };

PluginResult Status(Run run)
{
    var head = run.Error is not null ? "Could not send it" : $"{run.Status} {run.Reason}";

    var detail = run.Error is not null
        ? run.Error
        : $"{Human(run.Bytes)}  ·  {Took(run.Ms)}  ·  {(run.ContentType.Length == 0 ? "no content type" : run.ContentType.Split(';')[0])}";

    return new PluginResult
    {
        Id = "status:" + run.Id,
        Title = head,
        Subtitle = $"{run.Request.Method} {Cut(run.Request.Url, 55)}  ·  {detail}  ·  headers, body, again",
        Score = 500,
        Action = () => Copy(run.Request.Url)
    };
}

string VerbHelp(string verb, Run run) => verb switch
{
    "headers" => $"the {run.Headers.Count} response headers",
    "body" => $"the {Human(run.Bytes)} body, as it came",
    _ => "send the same command again"
};

List<PluginResult> Result(Run run, string keyword, string filter)
{
    var rows = new List<PluginResult> { Status(run) };
    if (run.Error is not null) return rows;

    // A filter that is the start of a verb offers the verb as well as the matching fields: "h"
    // is both the way to the headers and the start of plenty of field names.
    var verbScore = 480;
    foreach (var verb in Verbs)
    {
        if (filter.Length == 0 || !verb.StartsWith(filter, StringComparison.OrdinalIgnoreCase)) continue;
        rows.Add(Go($"verb:{run.Id}:{verb}", $"{keyword} {run.Id} {verb}", VerbHelp(verb, run),
                    $"{keyword} {run.Id} {verb}", verbScore--));
    }

    var fields = Fields(run);

    if (fields is null)
    {
        if (run.Bytes == 0) { rows.Add(Note("The answer has no body", "nothing came back to show", 300)); return rows; }
        if (!run.BodyIsText) { rows.Add(Note("The body is not text", $"{Human(run.Bytes)} of {run.ContentType}", 300)); return rows; }

        rows.Add(Note("The body is not JSON", $"{Human(run.Bytes)}  ·  \"{keyword} {run.Id} body\" shows it as it came", 300));

        var line = 290;
        foreach (var text in run.Body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(5))
            rows.Add(Copyable($"line:{run.Id}:{line}", Cut(text, 110), "a line of the body  ·  Enter to copy", line--));

        return rows;
    }

    var matching = filter.Length == 0
        ? fields
        : fields.Where(f => f.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || f.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

    if (matching.Count == 0)
    {
        rows.Add(Note($"No field matches \"{filter}\"", $"{fields.Count} field(s) came back", 300));
        return rows;
    }

    // Only a handful fit on screen, so say how many there are rather than let the rest vanish.
    if (filter.Length == 0 && fields.Count > 7)
        rows.Add(Note($"{fields.Count} fields", $"type part of a name after {run.Id} to narrow them down", 470));

    var score = 300;
    foreach (var (path, value) in matching.Take(20))
        rows.Add(Copyable($"field:{run.Id}:{path}", Cut(value.Length == 0 ? "—" : value, 110),
                          $"{path}  ·  Enter to copy", score--));

    return rows;
}

List<PluginResult> Headers(Run run, string filter)
{
    var rows = new List<PluginResult> { Status(run) };

    var matching = run.Headers
        .Where(h => filter.Length == 0 || h.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (matching.Count == 0)
    {
        rows.Add(Note($"No header matches \"{filter}\"", $"{run.Headers.Count} came back", 300));
        return rows;
    }

    var score = 300;
    foreach (var (name, value) in matching.Take(20))
        rows.Add(Copyable($"header:{run.Id}:{name}", Cut(value, 110), $"{name}  ·  Enter to copy", score--));

    return rows;
}

List<PluginResult> Body(Run run)
{
    var rows = new List<PluginResult> { Status(run) };

    if (run.Bytes == 0) { rows.Add(Note("The answer has no body", "nothing came back to show", 300)); return rows; }
    if (!run.BodyIsText) { rows.Add(Note("The body is not text", $"{Human(run.Bytes)} of {run.ContentType}", 300)); return rows; }

    rows.Add(new PluginResult
    {
        Id = "body:" + run.Id,
        Title = Cut(run.Body, 140),
        Subtitle = run.Truncated
            ? $"the first {Human(BodyLimit)} of {Human(run.Bytes)}  ·  Enter copies that much"
            : $"{Human(run.Bytes)}  ·  Enter copies the whole body",
        Score = 300,
        Action = () => Copy(run.Body)
    });

    return rows;
}

List<PluginResult> Preview(string keyword, string command)
{
    var id = ShortId(command);
    lock (Gate) KnownCommands[id] = command;

    var req = Parse(command);
    var rows = new List<PluginResult>();

    if (req.Error is not null)
    {
        rows.Add(Note("Cannot read that command", req.Error));
        rows.Add(Go("example", $"{keyword} https://api.github.com/users/torvalds", "a request that works, to start from",
                    $"{keyword} https://api.github.com/users/torvalds", 400));
        return rows;
    }

    var body = req.Body is null ? "no body" : $"body of {Human(Encoding.UTF8.GetByteCount(req.Body))}";

    rows.Add(Go("send:" + id, $"Send {req.Method} {Cut(req.Url, 60)}",
                $"{req.Headers.Count} header(s)  ·  {body}  ·  Enter sends it", $"{keyword} {id}", 500));

    // An unknown flag is the one thing worth interrupting for: the request would go out meaning
    // something other than what was pasted.
    var warn = 490;
    foreach (var unknown in req.Unknown.Take(3))
        rows.Add(Note($"\"{unknown}\" is not understood here", "it will not be part of the request", warn--));

    var score = 300;
    foreach (var (name, value) in req.Headers.Take(4))
        rows.Add(Copyable($"pre:{name}", Cut(value, 100), $"{name}  ·  will be sent", score--));

    if (req.Body is not null)
        rows.Add(Copyable("pre:body", Cut(req.Body, 110), $"body  ·  {req.ContentType}", score--));

    return rows;
}

List<PluginResult> History(string keyword)
{
    var rows = new List<PluginResult>();

    lock (Gate)
    {
        if (Entries.Count == 0)
        {
            rows.Add(Go("first", $"{keyword} https://api.github.com/users/torvalds",
                        "paste or type a curl command  ·  Enter shows what would be sent",
                        $"{keyword} https://api.github.com/users/torvalds", 500));
            return rows;
        }

        var score = 400;
        foreach (var entry in Entries.Take(12))
        {
            var outcome = entry.Error.Length > 0
                ? entry.Error
                : $"{entry.Status}  ·  {Human(entry.Bytes)}  ·  {Took(entry.Ms)}";

            rows.Add(Go("past:" + entry.Id, $"{entry.Method} {Cut(entry.Url, 60)}",
                        $"{entry.Id}  ·  {outcome}  ·  {Ago(entry.When)}", $"{keyword} {entry.Id}", score--));
        }

        rows.Add(Go("clear", $"{keyword} clear", $"forget all {Entries.Count} of them", $"{keyword} clear", 100));
    }

    return rows;
}

List<PluginResult> Clear()
{
    int count;
    lock (Gate) count = Entries.Count;

    if (count == 0) return [Note("There is nothing cached", "no request has been sent yet")];

    return
    [
        new PluginResult
        {
            Id = "clear",
            Title = $"Forget {count} cached request(s)",
            Subtitle = "the command lines on disk and the answers in memory",
            Score = 500,
            Action = () =>
            {
                lock (Gate)
                {
                    Entries.Clear();
                    KnownCommands.Clear();
                    Runs.Clear();

                    try { if (File.Exists(HistoryFile)) File.Delete(HistoryFile); }
                    catch (Exception ex) { Log?.Warn("could not delete the history: " + ex.Message); }
                }
            }
        }
    ];
}

// ===== plugin =====

return Plugin.Create(async (query, cancellationToken) =>
{
    var keyword = string.IsNullOrEmpty(query.Keyword) ? "curl" : query.Keyword;
    var text = query.Search.Trim();

    if (text.Length == 0) return History(keyword);

    // "cl" is nobody's URL -- a URL needs a dot or a scheme -- so it is the start of "clear"
    if (!text.Contains(' ') && !text.Contains('.') && !text.Contains(':') && !text.Contains('/')
        && "clear".StartsWith(text, StringComparison.OrdinalIgnoreCase))
    {
        if (text.Equals("clear", StringComparison.OrdinalIgnoreCase)) return Clear();

        List<PluginResult> toClear = [Go("clear", $"{keyword} clear", "forget every cached request", $"{keyword} clear", 500)];
        return toClear;
    }

    if (!text.StartsWith('#')) return Preview(keyword, Normalize(text));

    var space = text.IndexOf(' ');
    var id = (space < 0 ? text : text[..space]).ToLowerInvariant();
    var tail = space < 0 ? "" : text[(space + 1)..].Trim();

    string? command;
    lock (Gate) KnownCommands.TryGetValue(id, out command);

    if (command is null)
    {
        List<PluginResult> lost = [Note($"\"{id}\" is not a request I know", "it may have been cleared"), .. History(keyword)];
        return lost;
    }

    var again = tail.Equals("again", StringComparison.OrdinalIgnoreCase);

    if (!Runs.TryGetValue(id, out var run) || (again && run.Done))
    {
        var wait = TooSoon();
        if (wait is not null)
        {
            List<PluginResult> holding = wait.Length == 0
                ? [new PluginResult
                   {
                       Id = "settle:" + id,
                       Title = "Ready to send",
                       Subtitle = Cut(command, 90),
                       RefreshAfterMs = MinGapMs,
                       Score = 500
                   }]
                : [Note("Not sending that yet", wait)];

            return holding;
        }

        run = Start(id, command);
        if (again) tail = "";
    }

    // The launcher gives a query three seconds; anything slower carries on in the background
    // and the row refreshes itself until the answer lands.
    if (!run.Done) await Task.WhenAny(run.Task, Task.Delay(2000, cancellationToken));
    if (!run.Done) return [Progress(run)];

    if (tail.Equals("headers", StringComparison.OrdinalIgnoreCase)) return Headers(run, "");
    if (tail.StartsWith("headers ", StringComparison.OrdinalIgnoreCase)) return Headers(run, tail[8..].Trim());
    if (tail.Equals("body", StringComparison.OrdinalIgnoreCase)) return Body(run);

    return Result(run, keyword, tail);
},
(context, cancellationToken) =>
{
    DataDir = context.DataDirectory;
    HistoryFile = Path.Combine(DataDir, "history.tsv");
    Log = context.Log;
    Load();

    return Task.CompletedTask;
});
