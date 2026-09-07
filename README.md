# PlugLauncher

A small Windows launcher where every feature comes from a **plugin**. Plugins are `.csx`
scripts: no build step, no DLLs, just a folder dropped into `plugins/`.

```
Alt+Space   →  the window opens
(empty)     →  your plugins, most recently used first, plus the settings button
type        →  results from every plugin that matches
Enter       →  run  |  Tab: complete  |  ↑↓: select  |  Esc: close
wrong layout →  «زاقخپث» is searched as «chrome»; Tab rewrites the box
```

## Download

The [Releases page](https://github.com/mul83rry/PlugLauncher/releases) has two downloads of the
same build — take either:

| | |
|---|---|
| `PlugLauncher-<version>-setup.exe` | Installer. Start menu shortcut, an entry in Add or remove programs, upgrades in place. Installs per user under `%LOCALAPPDATA%\Programs`, so it never asks for administrator rights. |
| `PlugLauncher-<version>-win-x64.zip` | Portable. Extract anywhere, run `PlugLauncher.exe`, delete the folder to be rid of it. |

Neither one needs administrator rights. The app writes to `%APPDATA%\PlugLauncher` and, only if
you turn on **Start with Windows**, to `HKCU\...\CurrentVersion\Run` — nothing else.

**Requirement: [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)**
— the runtime, not the SDK. Pick **.NET Desktop Runtime**, `x64`, from the "Run desktop apps"
column. The plain *.NET Runtime* and the *ASP.NET Core Runtime* are **not** enough, because WPF
only ships in the Desktop bundle. .NET 9 or older will not work either: the app does not roll
forward across a major version.

The installer checks for the runtime and offers the download page before it copies anything. If
you take the zip instead, Windows shows a dialog with a download link the first time you start
the app.

> **First run:** nothing here is code signed, so SmartScreen shows a blue "Windows protected
> your PC" box — for the installer and for the app. Click **More info → Run anyway**. The
> `SHA256SUMS.txt` attached to each release covers both downloads, so you can check what you
> got against it.

`Alt+Space` opens the window. To quit: the tray icon → **Exit**, or the **Exit PlugLauncher**
button in settings. Closing the window only hides it — the app stays in the tray so the hotkey
keeps working.

On the first run it asks whether to start with Windows, and does not ask again either way. The
**Start with Windows** checkbox in settings is where you change the answer; it writes to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so no administrator rights are needed and
nothing is left behind for other users of the machine.

### Updates

Once a day PlugLauncher asks GitHub whether a newer release exists. If there is one, a tray
notification names it and the **Settings** button keeps a dot until you restart. Nothing is
downloaded or installed — the button opens the release page, and you upgrade the same way you
installed: run the new `setup.exe`, or replace the folder you unzipped.

It is one request to `api.github.com`, it carries nothing about you, and settings has both a
**Check now** button and a **Check automatically** checkbox to switch it off. Turning it off is
the same as `"checkForUpdates": false` in `settings.json`. A check that fails is not recorded, so
being offline when you sign in does not use up the day's check.

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

> **Do not turn on `PublishSingleFile`.** In single file mode the assemblies live inside the
> executable and have no path on disk. `CsxPluginLoader` builds its Roslyn references from
> `TRUSTED_PLATFORM_ASSEMBLIES` and `Assembly.Location`, both of which come back empty there
> (the compiler warns with `IL3000`). The result is that no `.csx` plugin compiles at all.

## Releasing

1. Bump `<Version>` in `Directory.Build.props`.
2. Add a `## <version>` section to [CHANGELOG.md](CHANGELOG.md) — the workflow reads it and
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
| `src/PlugLauncher.App` | The WPF interface: the glass window, the global hotkey, settings, the tray icon |

## Paths

| What | Where |
|---|---|
| Your plugins | `%APPDATA%\PlugLauncher\plugins\` |
| Bundled plugins | `plugins\` next to the executable |
| Settings | `%APPDATA%\PlugLauncher\settings.json` |
| Usage stats | `%APPDATA%\PlugLauncher\usage.json` |
| Script compile cache | `%APPDATA%\PlugLauncher\cache\scripts\` |
| Log | `%APPDATA%\PlugLauncher\logs\plugLauncher.log` |
| Optional user theme | `%APPDATA%\PlugLauncher\theme\theme.xaml` |

## Plugins

Three ship with the app, so a fresh install is useful immediately without being full of things
you did not ask for:

| Keyword | Plugin | What it does |
|---|---|---|
| *(none)* | Programs | Start menu shortcuts |
| *(none)* | Calculator | Evaluates the query as an expression |
| `pw` | Password Generator | Length, character sets, PIN and hex modes |

The rest live in the store tab in settings, one click each:

| Keyword | Plugin | What it does |
|---|---|---|
| `st` | Steam Games | Finds and launches installed games |
| `ssh` | SSH Hosts | Hosts from `~/.ssh/config`, opens a terminal on the one you pick |
| `dev` | Dev Toolbox | uuid, base64, url, hashes, epoch, JSON, JWT, random bytes, slugs |
| `sys` | System | Lock, restart, the msc/cpl consoles, and memory/disk/IP read-outs |
| `kb` | Keyboard Layout | Text typed on the wrong layout — `kb sghl` gives `سلام`, using the layouts you actually have installed |

The store is a static index published at
[mul83rry.github.io/PlugLauncher](https://mul83rry.github.io/PlugLauncher/) — no server, no
account. See [docs/STORE.md](docs/STORE.md) for how packages are built and published.

The three bundled plugins are in the store as well. A plugin installed from the store lands in
`%APPDATA%\PlugLauncher\plugins\` and takes precedence over the copy next to the executable, so
installing one is how you update a bundled plugin without waiting for the next release.

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
  "icon": "assets/icon.png"
}
```

- With `keywords` set, the plugin only runs when the query starts with one of them (`mp something`).
  The keyword has to be the whole first word: `mp` and `mp x` match, `mpx` does not.
- With `keywords` empty the plugin is **global** and sees every query, the way `calculator` does.
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

## License

[MIT](LICENSE). Copyright (c) 2026 Hosein Asadi.

---

Development notes and the task log live in [TASKS.md](TASKS.md) (Persian).
