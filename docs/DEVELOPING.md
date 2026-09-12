# Developing PlugLauncher

Everything a contributor needs: building, releasing, the project layout, and the plugin API.
Using the app as an end user is covered in the [README](../README.md) — this file is the one
you read with an editor open.

## Building from source

```bash
dotnet build PlugLauncher.slnx
dotnet run --project src/PlugLauncher.App
```

To produce a release archive exactly the way CI does:

```powershell
./tools/build-release.ps1
```

It publishes framework-dependent for `win-x64`, zips the output as
`publish/PlugLauncher-<version>-win-x64.zip`, compiles `tools/PlugLauncher.iss` into
`publish/PlugLauncher-<version>-setup.exe`, and writes one `SHA256SUMS.txt` covering both. The
version comes from `Directory.Build.props`, which is the only place it is written down.

The installer step needs `ISCC.exe` from [Inno Setup](https://jrsoftware.org/isinfo.php). The
GitHub windows runner has it pre-installed; a machine without it gets a warning and the archive
only, which `-SkipInstaller` also does deliberately.

> **Do not turn on `PublishSingleFile`.** In single file mode the assemblies live inside
> the executable and have no path on disk. `CsxPluginLoader` builds its Roslyn references from
> `TRUSTED_PLATFORM_ASSEMBLIES` and `Assembly.Location`, both of which come back empty there
> (the compiler warns with `IL3000`). The result is that no `.csx` plugin compiles at all.

### Running on macOS

macOS works, but there is no download for it: an unsigned bundle is refused by Gatekeeper on any
machine that did not build it, and signing needs an Apple developer account. So build it there
yourself with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```bash
./tools/build-mac.sh
open publish/PlugLauncher.app
```

**Build the bundle; do not `dotnet run`.** `RegisterEventHotKey` only answers a process the
window server considers an application, and a bare binary started from a terminal is not one —
so `dotnet run` can leave you looking at a launcher that never opens, for a reason that has
nothing to do with the hotkey code.

The bundle is self-contained (~120 MB) and unsigned, which is fine on the machine that built it:
macOS does not quarantine what was built locally. Copy the `.app` to a second Mac and Gatekeeper
will refuse to start it.

The hotkey asks for **no Accessibility permission** — that one belongs to `CGEventTap`, which
sees every key on the system. Carbon is only handed the combination it registered, so there is
nothing for macOS to ask about. Option counts as Alt and Command as Win, so a hotkey saved on
Windows keeps working from the same `settings.json`.

`programs` is bundled but declares `windows` only, so on a Mac it loads as unsupported and shows
orange in the list. That is the intended outcome, not a failure: a Mac has no Start menu and no
`.lnk` files, so the plugin would have found nothing and said nothing about why. Its counterpart
is **Applications**, one click away in the store — same idea, reading `/Applications` instead.

Linux builds and runs but has no hotkey yet, so there is no way to open the window.

## Releasing

1. Bump `<Version>` in `Directory.Build.props`.
2. Add a `## <version>` section to [CHANGELOG.md](../CHANGELOG.md) — the workflow reads it and
   fails if it is missing.
3. Commit, then tag and push:

   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

The `Release` workflow builds on `windows-latest`, checks that the tag matches the version in
`Directory.Build.props`, and creates the GitHub release with the installer, the zip and the
checksum file attached. It fails if either artifact is missing rather than publishing half a
release.

Running the workflow by hand (**Actions → Release → Run workflow**) builds exactly the same
thing and leaves it as an artifact without creating a release, which is the way to check a
packaging change before committing to a tag.

## Project layout

| Project | Role |
|---|---|
| `src/PlugLauncher.Contracts` | The plugin contract (`IPlugin`, `PluginResult`, `PluginQuery`, `IPluginContext`) — all a plugin author ever sees |
| `src/PlugLauncher.Core` | Plugin discovery, Roslyn compilation with an on-disk cache, query execution, usage stats, settings |
| `src/PlugLauncher.Platform` | The parts that differ per operating system behind one door: global hotkey, start-at-login, opening files |
| `src/PlugLauncher.App` | The interface, in Avalonia: the glass window, settings, the tray icon |

Paths worth knowing while developing:

| What | Where |
|---|---|
| User plugins | `%APPDATA%\PlugLauncher\plugins\` |
| Bundled plugins | `plugins\` next to the executable |
| Plugin data | `%APPDATA%\PlugLauncher\data\<plugin-id>\` |
| Settings | `%APPDATA%\PlugLauncher\settings.json` |
| Usage stats | `%APPDATA%\PlugLauncher\usage.json` |
| Script compile cache | `%APPDATA%\PlugLauncher\cache\scripts\` |
| Log | `%APPDATA%\PlugLauncher\logs\plugLauncher.log` |
| Optional user theme | `%APPDATA%\PlugLauncher\theme\theme.json` — a flat `{ "AccentBrush": "#FF4C8DFF" }`; the key names are the ones in `Theme.axaml` |

## Writing a plugin

```
my-plugin/
  plugin.json     # manifest
  main.csx        # code
  assets/         # icons
```

**plugin.json**

```json
{
  "id": "com.you.my-plugin",
  "name": "My Plugin",
  "description": "One line about it",
  "version": "1.0.0",
  "entry": "main.csx",
  "keywords": [ "mp" ],
  "usage": [
    { "example": "mp something", "description": "what that does" }
  ],
  "icon": "assets/icon.png",
  "platforms": [ "windows", "macos", "linux" ],
  "minCore": "1.6.0"
}
```

- With `keywords` set, the plugin only runs when the query starts with one of them (`mp something`).
  The keyword has to be the whole first word: `mp` and `mp x` match, `mpx` does not.
- With `keywords` empty the plugin is **global** and sees every query, the way `calculator` does.
- `usage` is the plugin's own cheat sheet. The moment the user has typed the keyword and nothing
  else, those lines appear under the results; Enter on one puts the example in the search box
  instead of running it. Six is the most that show. Nobody has to remember your syntax, and you
  write no code for it.
- `platforms` says where the plugin works: `windows`, `macos`, `linux`. Leave it out and it is
  assumed to run everywhere, which is right for a plugin that only shuffles text around. Name
  them when the plugin really is tied to one — the registry, the Start menu, a particular exe.
- `minCore` is the oldest launcher the plugin works on. A plugin is compiled when it loads, so
  using something an older core does not have is a compile error rather than a missing feature;
  this says so in advance.
- Either one that does not fit is caught before compiling: the launcher shows the plugin as **Not
  for this system** with the reason, and the store greys out Install instead of handing you a
  plugin that breaks on arrival.
- A keyword has one owner. If two plugins claim the same one, the older install keeps it and the
  newer plugin is not loaded at all — settings shows it as **Keyword taken** with the name of the
  plugin holding it, and a tray notification says so at startup. Change `keywords` and hit
  **Reload** to bring it back.

**main.csx** — the last line has to return an `IPlugin`:

```csharp
return Plugin.Create(query =>
{
    var term = query.Search.Trim();   // the text after the keyword

    return new[]
    {
        new PluginResult
        {
            Id       = "unique-row-id",         // used for the usage stats
            Title    = $"Hello {term}",
            Subtitle = "the second line of the row",
            IconPath = "assets/icon.png",       // relative to the plugin folder, or absolute
            Score    = 100,                     // higher sorts nearer the top
            Fraction = 0.38,                    // optional: a bar behind the row, 0 to 1
            Action   = () => Process.Start(new ProcessStartInfo("https://example.com")
                             { UseShellExecute = true })
        }
    };
});
```

There is an async form with an initializer too:

```csharp
return Plugin.Create(
    query: async (q, ct) => { /* ... */ },
    initialize: async (ctx, ct) => { ctx.Log.Info(ctx.PluginDirectory); });
```

### Things worth knowing

- **Compile cache**: each plugin's compiled output is kept in `cache\scripts`, keyed on a hash
  of its `.csx` files and `plugin.json`. Edit the code and the hash changes, so it recompiles
  by itself.
- **Isolation**: a plugin that throws or runs long only removes itself from the results
  (`queryTimeoutMs`, three seconds by default) and the reason goes to the log.
- **References**: the whole framework and `PlugLauncher.Contracts` are available to the script.
  Extra DLLs go in the manifest's `references` array.
- **Compile errors** appear in settings, next to the plugin, with a line number.
- **`Fraction`** on a result draws a faint bar behind the row. Use it for anything that is a
  share of a whole; leave it `null` for everything else.
- **`ReplaceQuery`** on a result makes Enter put that text in the search box instead of running
  anything, which is how a plugin offers drill-down: the Disk Usage plugin is nothing but this.
  Tab accepts the selected one too, and when it continues what is already typed the rest of it
  shows as a ghost in the box. Make it the whole command, not a fragment — the row is then both
  the answer and a lesson in the syntax.
- **`RefreshAfterMs`** on a result asks the launcher to run the same query again after that many
  milliseconds. Return a progress row with it while the real work runs in the background, and
  drop it once the answer is ready — the list fills in by itself. The launcher will not go faster
  than 250 ms, gives up on any one query after two minutes, and stops the moment the window
  closes, so a plugin that always asks for a refresh cannot spin in the background.
- **`DetailText`** on a result makes Enter open the text in a view instead of running: a
  read-only monospace panel with a copy button in its corner — the curl answers and the terminal
  transcript live here. `DetailTitle` names it, and `DetailSyntax = "json"` colors the text like
  an editor (past 20,000 characters it falls back to plain, for speed). Keep an `Action` on the
  row too, so the host of a user with an older launcher still does something sensible — but set
  `minCore` honestly, because the properties themselves have to exist for the script to compile.
