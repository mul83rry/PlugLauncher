// A terminal that remembers. Keyword: term
//
//   term                 -> the session (cwd, the whole transcript) and the commands you ran before
//   term dir             -> "Run `dir`" (Enter) -> term #a1b2 (Enter) -> it runs
//   term #a1b2           -> run that command again
//   term clear           -> empty the transcript and forget the history
//
// One shell lives for as long as the launcher does. A command does not start a process, it is
// typed into the shell that is already there — so `cd`, environment variables, `conda activate`
// and everything else a shell keeps survive from one command to the next, even though each one
// was typed in the launcher a minute apart. The session dies with the launcher; nothing is left
// running after it.
//
// Output is shown in the launcher's text view: the whole transcript, one command after another,
// like a small terminal. Copy in the corner gives the transcript out.
//
// How a command knows it has finished: redirected pipes have no end-of-command mark, so after
// every command a second line is sent that prints a sentinel carrying the current directory —
// the line that says "done" also says "where". A command that never finishes (a server, ping -t)
// is cut off after a while: what came is shown, the note says so, and the next command restarts
// a clean shell rather than typing into one that is still busy.
//
// On Windows the shell is powershell.exe. UTF-8 output is asked for first, but a shell with
// redirected pipes has no console and the setting can be refused — it is tried quietly and when
// it fails non-ASCII in an answer may come back garbled. What is typed as the command itself is
// safest in ASCII either way: PowerShell decodes its input with the console's code page, and a
// Persian filename on that line may not survive the trip.
//
// What is kept on disk: the command lines, newest first, in terminal-history.txt under the
// launcher's data directory. Output is not written anywhere — it lives in memory until the
// launcher closes. "term clear" wipes both.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

// ===== what the host gives us =====

string DataDir = "";
IPluginLogger? Log = null;

// ===== the session =====

const int HistoryLimit = 50;
const int ColdStartMs = 30_000;
const int WarmMs = 20_000;
const int TranscriptLimit = 32 * 1024;
const string Sentinel = "__PLUGLAUNCHER_TERM_DONE__";

/// <summary>The one shell process, and everything it has said so far.</summary>
sealed class TermSession
{
    public Process? Shell;
    public string Cwd = "";
    public int Commands;
    public readonly StringBuilder Transcript = new();
    public readonly object Tape = new();
}

/// <summary>One command on its way through the shell.</summary>
sealed class TermRun
{
    public string Id = "";
    public string Command = "";
    public readonly StringBuilder Output = new();
    public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly object Buffer = new();
    public DateTime Started;
    public double Ms;
    public bool TimedOut;
    public string Cwd = "";
    public string? Error;
}

readonly ConcurrentDictionary<string, TermRun> Runs = new(StringComparer.Ordinal);
readonly Dictionary<string, string> KnownCommands = new(StringComparer.Ordinal);
readonly List<string> History = new();
readonly object Gate = new();
readonly TermSession Term = new();

TermRun? _active;
string _historyFile = "";
bool _loaded;

bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

string Prompt()
{
    lock (Term.Tape) return Term.Cwd.Length == 0 ? "term>" : Term.Cwd + ">";
}

// ===== small helpers =====

string Cut(string text, int max)
{
    var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return flat.Length > max ? flat[..(max - 1)] + "…" : flat;
}

string Took(double ms) => ms >= 1000 ? $"{ms / 1000:0.##} s" : $"{ms:0} ms";

string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

string ShortId(string command)
    => "#" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(command)))[..4].ToLowerInvariant();

PluginResult Note(string title, string subtitle = "", int score = 500)
    => new() { Id = "note:" + title, Title = title, Subtitle = subtitle, Score = score };

PluginResult Go(string id, string title, string subtitle, string command, int score)
    => new() { Id = id, Title = title, Subtitle = subtitle, Score = score, ReplaceQuery = command };

// ===== the transcript =====

void Say(string line)
{
    lock (Term.Tape)
    {
        Term.Transcript.AppendLine(line);
        if (Term.Transcript.Length > TranscriptLimit)
            Term.Transcript.Remove(0, Term.Transcript.Length - TranscriptLimit);
    }
}

string Tape()
{
    lock (Term.Tape) return Term.Transcript.ToString();
}

// ===== the shell =====

string SentinelLine => OnWindows
    ? "Write-Output (\"" + Sentinel + "\" + (Get-Location).Path)"
    : "echo \"" + Sentinel + "$PWD\"";

Process StartShell()
{
    var info = new ProcessStartInfo
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        // UTF-8 بدون BOM: Encoding.UTF8 یک BOM اول استریم می‌نویسد و شل آن را جزئی از اولین
        // دستور می‌داند ("The term 'try' is not recognized") — با BOM خاموش، دستورها سالم می‌رسند.
        StandardInputEncoding = new UTF8Encoding(false)
    };

    if (OnWindows)
    {
        // -Command - reads commands from stdin one line at a time, with no prompt and no banner
        info.FileName = "powershell.exe";
        info.Arguments = "-NoProfile -NoLogo -NonInteractive -ExecutionPolicy Bypass -Command -";
    }
    else
    {
        info.FileName = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/bash";
    }

    var shell = Process.Start(info)!;
    shell.OutputDataReceived += OnLine;
    shell.ErrorDataReceived += OnLine;
    shell.BeginOutputReadLine();
    shell.BeginErrorReadLine();

    return shell;
}

/// <summary>
/// Every line the shell prints. During a run it feeds that run until the sentinel arrives — the
/// line that both ends the command and reports the directory. Lines outside any run (a banner,
/// the tail of a cut-off command) go straight to the transcript.
/// </summary>
void OnLine(object sender, DataReceivedEventArgs e)
{
    var line = e.Data;
    if (line is null) return;

    TermRun? run;
    lock (Gate) run = _active;

    if (run is null || run.Done.Task.IsCompleted)
    {
        Say(line);
        return;
    }

    if (line.StartsWith(Sentinel, StringComparison.Ordinal))
    {
        run.Cwd = line[Sentinel.Length..];
        run.Done.TrySetResult();
        return;
    }

    lock (run.Buffer) run.Output.AppendLine(line);
}

/// <summary>Sends one command and waits for its sentinel. Never throws: a failure lands in the run.</summary>
async Task<TermRun> Send(string command)
{
    var run = new TermRun { Id = ShortId(command), Command = command, Started = DateTime.UtcNow };
    var cold = Term.Shell is null;

    lock (Gate)
    {
        if (_active is { } busy)
        {
            // A cut-off command may still be holding the pipe (ping -t); a live one certainly is.
            if (!busy.TimedOut && !busy.Done.Task.IsCompleted)
            {
                run.Error = "the terminal is still running a command";
                run.Done.TrySetResult();
                return run;
            }

            // The shell is in no state to take another command — start over, transcript and all
            TryKillShell();
            cold = true;
        }

        _active = run;
    }

    Runs[run.Id] = run;

    try
    {
        var shell = Term.Shell;
        if (shell is null || shell.HasExited)
        {
            shell = StartShell();
            Term.Shell = shell;
            if (OnWindows) await shell.StandardInput.WriteLineAsync("try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }");
        }

        Say("");
        Say(Prompt() + " " + command);

        await shell.StandardInput.WriteLineAsync(command);
        await shell.StandardInput.WriteLineAsync(SentinelLine);

        var budget = cold ? ColdStartMs : WarmMs;
        var finished = await Task.WhenAny(run.Done.Task, Task.Delay(budget)) == run.Done.Task;
        if (!finished)
        {
            run.TimedOut = true;
            run.Done.TrySetResult();
        }
    }
    catch (Exception ex)
    {
        run.Error = ex.Message;
        run.Done.TrySetResult();
    }

    run.Ms = (DateTime.UtcNow - run.Started).TotalMilliseconds;

    string output;
    lock (run.Buffer) output = run.Output.ToString();
    if (output.Length > 0) Say(output.TrimEnd());

    if (run.Cwd.Length > 0) Term.Cwd = run.Cwd;
    lock (Term.Tape) Term.Commands++;

    lock (Gate) { if (_active == run) _active = null; }

    Remember(command);
    return run;
}

void TryKillShell()
{
    try { Term.Shell?.Kill(entireProcessTree: true); }
    catch (Exception ex) { Log?.Warn("could not stop the old shell: " + ex.Message); }

    Term.Shell = null;
    Term.Cwd = "";
}

// ===== what is remembered =====

void Remember(string command)
{
    lock (Gate)
    {
        History.RemoveAll(h => h.Equals(command, StringComparison.Ordinal));
        History.Insert(0, command);
        if (History.Count > HistoryLimit) History.RemoveRange(HistoryLimit, History.Count - HistoryLimit);
        KnownCommands[ShortId(command)] = command;

        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllLines(_historyFile, History);
        }
        catch (Exception ex) { Log?.Warn("could not write the history: " + ex.Message); }
    }
}

void Load()
{
    _historyFile = Path.Combine(DataDir, "terminal-history.txt");

    try
    {
        if (File.Exists(_historyFile))
            lock (Gate) History.AddRange(File.ReadAllLines(_historyFile).Where(l => l.Length > 0));
    }
    catch (Exception ex) { Log?.Warn("could not read the history: " + ex.Message); }
}

// ===== views =====

PluginResult Progress(TermRun run)
    => new()
    {
        Id = "waiting:" + run.Id,
        Title = $"{Cut(run.Command, 70)} …",
        Subtitle = $"running  ·  {Took((DateTime.UtcNow - run.Started).TotalMilliseconds)}  ·  {Prompt()}",
        RefreshAfterMs = 300,
        Score = 500
    };

PluginResult Status(TermRun run)
{
    string head;
    string detail;

    if (run.Error is not null) { head = "Could not run it"; detail = run.Error; }
    else if (run.TimedOut) { head = $"Cut off after {Took(run.Ms)}"; detail = "the command may still be going — the next one starts a fresh shell"; }
    else
    {
        int lines;
        lock (run.Buffer) lines = CountLines(run.Output);
        head = lines == 0 ? $"Done  ·  nothing came back  ·  {Took(run.Ms)}" : $"Done  ·  {lines} line(s)  ·  {Took(run.Ms)}";
        detail = Prompt();
    }

    string output;
    lock (run.Buffer) output = run.Output.ToString();

    return new PluginResult
    {
        Id = "status:" + run.Id,
        Title = head,
        Subtitle = $"{detail}  ·  run it again: term {run.Id}",
        Score = 500,
        Action = () => Clipboard.Copy(output)
    };
}

int CountLines(StringBuilder builder)
    => builder.Length == 0 ? 0 : builder.ToString().Count(c => c == '\n') + (builder[^1] == '\n' ? 0 : 1);

PluginResult TheTerminal()
{
    int commands;
    lock (Term.Tape) commands = Term.Commands;

    return new PluginResult
    {
        Id = "terminal",
        Title = "The terminal",
        Subtitle = commands == 0
            ? $"{Prompt()}  ·  nothing run yet  ·  Enter opens it"
            : $"{Prompt()}  ·  {commands} command(s)  ·  Enter opens it",
        Score = 490,
        DetailTitle = "term  ·  the session stays between commands",
        DetailText = Tape() + Environment.NewLine + Prompt(),
        Action = () => Clipboard.Copy(Tape())
    };
}

List<PluginResult> Home()
{
    var rows = new List<PluginResult> { TheTerminal() };

    lock (Gate)
    {
        if (History.Count == 0)
        {
            rows.Add(Go("first", "Run `dir`", "a command to start from  ·  Enter fills the box", "term dir", 400));
            rows.Add(Note("Type a command after term", "one shell keeps running between commands — cd sticks, variables stick", 300));
            return rows;
        }

        var score = 400;
        foreach (var command in History.Take(12))
            rows.Add(Go("past:" + ShortId(command), Cut(command, 70),
                        "run it again  ·  Enter fills the box", "term " + command, score--));

        rows.Add(Go("clear", $"Forget all {History.Count} command(s)", "the history on disk and the transcript", "term clear", 100));
    }

    return rows;
}

List<PluginResult> Clear()
{
    int count;
    lock (Gate) count = History.Count;

    if (count == 0) return [Note("There is nothing to forget", "no command has been run yet")];

    return
    [
        new PluginResult
        {
            Id = "clear",
            Title = $"Forget {count} command(s) and empty the transcript",
            Subtitle = "the history on disk; the shell keeps running",
            Score = 500,
            Action = () =>
            {
                lock (Gate)
                {
                    History.Clear();
                    KnownCommands.Clear();
                    Runs.Clear();

                    try { if (File.Exists(_historyFile)) File.Delete(_historyFile); }
                    catch (Exception ex) { Log?.Warn("could not delete the history: " + ex.Message); }
                }

                lock (Term.Tape) Term.Transcript.Clear();
            }
        }
    ];
}

List<PluginResult> Run(string keyword, string command)
{
    var id = ShortId(command);
    lock (Gate) KnownCommands[id] = command;

    return
    [
        Go("run:" + id, $"Run `{Cut(command, 60)}`",
           $"{Prompt()}  ·  Enter runs it in the session", $"{keyword} {id}", 500),
        .. Matches(keyword, command)
    ];
}

/// <summary>Old commands that contain what was typed — the "it worked last time" list.</summary>
List<PluginResult> Matches(string keyword, string needle)
{
    var rows = new List<PluginResult>();

    lock (Gate)
    {
        var score = 300;
        foreach (var command in History.Where(c => c.Contains(needle, StringComparison.OrdinalIgnoreCase)).Take(6))
            rows.Add(Go("match:" + ShortId(command), Cut(command, 70), "run it again", $"{keyword} " + ShortId(command), score--));
    }

    return rows;
}

// ===== plugin =====

return Plugin.Create(async (query, cancellationToken) =>
{
    var keyword = string.IsNullOrEmpty(query.Keyword) ? "term" : query.Keyword;
    var text = query.Search.Trim();

    if (text.Length == 0) return Home();

    if (!text.Contains(' ') && "clear".StartsWith(text, StringComparison.OrdinalIgnoreCase))
    {
        if (text.Equals("clear", StringComparison.OrdinalIgnoreCase)) return Clear();

        return [Go("clear", $"{keyword} clear", "forget every command and empty the transcript", $"{keyword} clear", 500)];
    }

    if (!text.StartsWith('#')) return Run(keyword, text);

    var space = text.IndexOf(' ');
    var id = (space < 0 ? text : text[..space]).ToLowerInvariant();

    string? command;
    lock (Gate) KnownCommands.TryGetValue(id, out command);

    if (command is null)
        return [Note($"\"{id}\" is not a command I know", "it may have been cleared"), .. Home()];

    if (!Runs.TryGetValue(id, out var run) || run.Done.Task.IsCompleted && (run.TimedOut || run.Error is not null))
        run = await Send(command);

    // The launcher gives a query three seconds; a command that needs longer carries on and the
    // row refreshes itself until the sentinel lands.
    if (!run.Done.Task.IsCompleted) await Task.WhenAny(run.Done.Task, Task.Delay(2000, cancellationToken));
    if (!run.Done.Task.IsCompleted) return [Progress(run), TheTerminal()];

    if (run.Error is not null) return [Status(run), TheTerminal()];

    return [Status(run), TheTerminal()];
},
(context, cancellationToken) =>
{
    DataDir = context.DataDirectory;
    Log = context.Log;
    Load();

    return Task.CompletedTask;
});
