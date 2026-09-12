# Changelog

The release workflow reads the section matching the tag out of this file and uses it as the
release notes, so every version needs a `## <version>` heading here before it can be tagged.

## Unreleased

**Keyboard Layout converts mixed text**

`kb` used to reject a query outright the moment one character could not have come from the source
layout, so text holding both scripts at once — `sghl چطوری` — produced no rows: the Persian half
killed the conversion of the Latin half. Now a character the source layout cannot type is passed
through unchanged instead of poisoning the whole candidate, and `kb sghl چطوری` gives
`سلام چطوری`. A text the layout cannot explain at all is still rejected, because a conversion
where nothing changed is not a conversion.

**New plugin: Applications** (no keyword, from the store — macOS only)

`programs` reads the Start menu, which a Mac does not have, so there it loaded as unsupported and
would have found nothing anyway. **Applications** is the other half. It walks `/Applications`,
`/System/Applications` and `~/Applications`, stops at the first `.app` on each branch so the
helper bundles buried inside `Safari.app` stay out of the list, reaches into Utilities and the
folders vendors make for themselves, and opens what you pick with `open`. Like `programs` it has
no keyword, so it answers whatever you type.

They stayed two plugins instead of one with a platform switch inside. Nothing but the scoring is
shared between reading `.lnk` shortcuts and reading `.app` bundles, so a single file would only
have been longer.

Each row shows the plugin's own icon rather than the application's. A Mac keeps an app icon as an
`.icns` inside the bundle and the launcher has no decoder for that format, so asking per row
would have failed on every row.

**Fixed: turning one plugin off in settings could turn all of them off**

The Enabled checkbox is wired to `IsCheckedChanged`, which also fires when the value is changed in
code — and refreshing the list replaces it whole. So Avalonia threw the old rows away, each
checkbox lost its binding and fell back to unchecked, and every one of those raised the handler
again: which switched that plugin off and asked for another refresh, from inside the refresh that
was still running. The loud half was a run of `ArgumentOutOfRangeException` in the log as the
container list went out of sync with the panel. The quiet half was plugins switching themselves
off. The handler now stands down while the list is being rebuilt.

This was not a macOS bug — the same trace is in the Windows log. It only needed someone to open
the settings window and use the toggle.

## 1.5.0 — 2026-09-08

**One interface for Windows, macOS and Linux**

The window, the settings, the tray icon and the list are drawn by Avalonia now instead of WPF.
The app project no longer targets `net10.0-windows`, so nothing in the four projects is bound to
one operating system any more; what is left of Windows lives behind `PlugLauncher.Platform`
together with the macOS and Linux versions of the same three jobs.

This is not a new coat of paint. The launcher looks and behaves the same — the glass window, the
grey completion behind what you type, the keys, the segmented tabs. A few things underneath had
to change to stop being Windows-only:

- **The runtime you need is smaller.** The plain **.NET 10 Runtime** is now enough; the Desktop
  Runtime was only ever needed because of WPF. If you already have the Desktop Runtime, nothing
  to do — it contains the plain one.
- **Notices come as a small panel in the corner** instead of a tray balloon. A balloon is a
  Windows idea and there is no way to raise one from a portable toolkit. The panel stays for ten
  seconds, says the same thing, and clicking it does the same thing.
- **Only one copy runs at a time, held by a lock file** rather than a named mutex — a named
  mutex is another Windows-only idea. If the app is killed, the system releases the file, so the
  next start is never blocked by a stale lock.
- **The user theme is `theme.json` instead of `theme.xaml`.** The old file was handed straight
  to the WPF theme engine, and that engine is gone; a flat list of `"key": "#colour"` does the
  same job and reads the same on all three systems. The key names have not changed.
- **`Ask.Confirm` in the plugin contract.** The `system` plugin called
  `System.Windows.MessageBox` directly to ask before restarting; now it asks the launcher, which
  is the one that knows how to draw a dialog. Same as `Clipboard`.
- Settings is no longer a modal dialog, and the launcher steps out of the way when it opens —
  the launcher is always on top, so staying would mean sitting on top of the settings.

macOS works. Linux builds and runs but still has no global hotkey, so there is no way to open the
window there yet.

**macOS can open the window**

The global hotkey is registered through Carbon's `RegisterEventHotKey`. That API needs **no
Accessibility permission** — the permission everyone associates with macOS hotkeys belongs to
`CGEventTap`, which sees every key on the system; this one is only handed the combination it
asked for. Option is read as Alt and Command as Win, so a hotkey saved on Windows keeps working
on the same `settings.json`.

Two smaller pieces came with it. On macOS a window does not come forward, the *application*
does, so showing the launcher now activates the app — without that the window appears but the
keyboard stays with whatever you were using, which is no launcher at all. And the pointer's
position is read from Core Graphics, so the window opens on the display you are looking at.

`tools/build-mac.sh` builds `PlugLauncher.app`. The bundle is not packaging polish: Carbon only
gives the hotkey to a process the window server counts as an application, and a binary started
from a terminal is not one — so `dotnet run` can produce a launcher that never opens for a reason
that has nothing to do with the hotkey. The bundle is unsigned, which is fine on the machine that
built it and refused by Gatekeeper anywhere else.

This was tested on a Mac and the launcher opens and runs there. There is no macOS download in
this release: the bundle would have to be signed and notarised before Gatekeeper would let
anyone start a copy they did not build themselves, and that needs an Apple developer account.
Until then, build it yourself with `./tools/build-mac.sh` — see the README.

Linux still has no hotkey.

**Three more plugins work on all three systems**

`disk-usage`, `ssh-hosts` and `steam-games` used to be marked Windows-only. Their logic never
was: a folder scan, an `~/.ssh/config` parser and Steam's own library files read the same
everywhere. What tied each of them down was one line about how the system does something.

- **`Shell.Open` and `Shell.Reveal` in the plugin contract**, the third pair after `Clipboard`
  and `Ask`. Plugins were calling `explorer.exe /select,` to show a file. Opening a folder is
  not a Windows idea, but `explorer.exe` is — so the launcher does it now and the plugin just
  asks.
- **Disk Usage** no longer assumes a backslash or a drive letter, and skips the pseudo-drives
  that Linux presents as filesystems — `/proc`, `/sys` and a pile of tmpfs mounts. A drive with
  no label is listed by its kind rather than dropped.
- **SSH Hosts** opens Windows Terminal or PowerShell on Windows, Terminal.app on macOS, and
  tries the usual terminals in turn on Linux.
- **Steam Games** finds the Steam folder in the registry on Windows, under
  `~/Library/Application Support` on macOS, and in the three usual places on Linux, flatpak
  included.

`programs` and `system` stay Windows-only. The store already says so, next to a disabled Install
button, rather than hiding them.

**A result row can carry a bar**

`PluginResult` has a new `Fraction` (0 to 1). The launcher draws it as a faint bar behind the
row, the way Explorer shows how full a drive is. It is there for anything that is a share of a
whole — disk space, battery, a download — and any plugin can set it.

**New plugin: Disk Usage** (`du`, from the store — needs this version)

What is taking the space, without leaving the launcher:

- `du` lists the drives with their used share as a bar.
- `du C:\` lists the folders inside, biggest first; Enter on one goes into it.
- `du C:\ types` groups the same bytes by kind of file — Video, Archives, Programs, Code, and so
  on — and Enter on a kind lists its extensions, and Enter on an extension lists the biggest
  files of that kind. Enter on a file shows it in Explorer.
- A scan keeps only sums, so a whole drive costs nothing in memory. It costs seconds instead, and
  a scan that does not finish inside the query keeps running in the background while the row
  counts up; the answer appears on its own when it is ready.
- Numbers are kept for five minutes. After that the old ones are still shown, marked as old,
  while a fresh scan runs.

**New plugin: Curl** (`curl`, from the store — needs this version)

Send a request without leaving the launcher, and read the answer instead of squinting at it.

- `curl https://api.github.com/users/torvalds` shows what would be sent — method, URL, headers,
  body — and Enter sends it. Nothing goes out while you type.
- The answer comes back as rows: the status and how long it took, then every field of the JSON,
  flattened, so `nested.deep` and `tags[0]` are each a row you can copy with Enter.
- `curl #a3f login` keeps only the fields that match, `curl #a3f headers` lists the response
  headers, `curl #a3f body` gives the body as it came, and `curl #a3f again` sends it once more.
  The short id is a hash of the command, so the same command always has the same one.
- `curl` on its own is everything you have sent, newest first, with how each one went.
  `curl clear` forgets all of it.
- The command line is parsed here rather than handed to curl.exe: `-X -H -d --data --json -u -A
  -b -e -L -I -G --url` and the noise flags that get copied along. Anything else is reported
  rather than silently dropped. A redirect is shown as a redirect unless you passed `-L`.
- On disk: the command lines and how each last went. Not the response bodies — a body is usually
  where the secrets are, and it lives in memory only until the launcher closes. A token you put
  in a command line is part of that command line.

**Slow results arrive by themselves**

A plugin can put `RefreshAfterMs` on a result to say its work is still going. The launcher runs
the same query again after that long and swaps the row for whatever came back, so nothing has to
be pressed twice to see a long job finish. Three limits keep a faulty plugin from spinning: never
faster than 250 ms, two minutes of it per typed query, and everything stops when the window
closes.

**A plugin says which systems and which launcher it needs**

Two optional fields in `plugin.json`: `platforms` (`windows`, `macos`, `linux` — empty means
everywhere) and `minCore` (the oldest launcher it works on, like `1.5.0`). A plugin that does not
fit is not compiled at all; settings shows it as **Not for this system** with the reason, and the
store greys out Install rather than handing over a plugin that breaks the moment it lands. Older
launchers ignore both fields, the way they already ignore `usage`.

**A plugin gets the clipboard from the launcher**

`Clipboard.Copy(text)` and `Clipboard.Text()` in `PlugLauncher.Contracts`. Plugins used to call
`System.Windows.Clipboard` themselves, which tied a plugin like the calculator — nothing about it
is Windows — to WPF, and left the same three-attempt retry copied into seven scripts. It lives in
the launcher now, in one place. A plugin that copies needs this version.

**Pasting something that is more than one line keeps all of it**

The search box is a single line, and a single-line box on Windows keeps the first line of a paste
and throws the rest away without saying so. A curl command copied out of Postman lost every
header. The lines are now joined into one instead, so what you pasted is what is in the box.

**Suggestions that follow what you are typing**

A row that offers a next step now says the whole command, and Tab takes it. Where it continues
what is in the box the rest appears as a ghost, so you can see the ending before you commit to
it. Disk Usage uses this the whole way down: `du C:\ ` names both `types` and `ext`, `du C:\ ext`
lists the extensions that are actually in that folder, typing `.z` narrows them to `.zip`, and
half a word — `du C:\ ty` — offers the verb it is the start of. Nothing has to be known in
advance and nothing has to be typed exactly.

**Plugins can carry their own cheat sheet**

`plugin.json` takes a `usage` list of examples. Type a plugin's keyword and pause, and its
examples appear under the results; Enter on one writes it into the search box instead of running
it. So `du` shows the way to `du C:\ types Archives` without anybody memorising it, and `sys`,
`ssh`, `kb` and `st` gained the same. Up to six show, and they never take the place of a real
result.

**Dev Toolbox stopped going blank**

`dev b64` on its own used to answer with the base64 of nothing, which is an empty row. Now every
command that needs something to work on says what to type instead: `dev b64` offers
`dev b64 hello`, and Tab takes it. Half a name works the same way — `dev b` offers `b64` and
`b64d` together, `dev u` offers `uuid` and `url` — and `dev qqq` says no command starts with that
rather than quietly listing everything. The decoders and `rand` moved one keystroke away so the
list of commands fits on screen; before this, `slug` was on that list and never visible.

## 1.4.0 — 2026-09-07

**It tells you when there is a new version**

PlugLauncher had no way of saying a release existed; the only way to find out was to go and look
at the repository. Now it checks once a day, in the background, and says so if there is one.

- A tray notification names the new version. Clicking it opens the release page.
- The notification is gone in ten seconds, so the **Settings** button also keeps a dot on it for
  the rest of the session — otherwise an update found while you were away from the machine would
  never be seen.
- Settings has the version it is running, a **Check now** button, and a **Check automatically**
  checkbox to turn the whole thing off.
- **Nothing is downloaded or installed.** The button opens the release page and stops there,
  because installing over an installer install and replacing an unzipped folder are two different
  operations and the app cannot tell which one you did.

What it costs: one `GET` to `api.github.com` per day, with no data about you in it and no account
involved. A check that fails — no connection, GitHub unreachable — is not recorded as a check, so
being offline at boot does not cost you the next day's.

## 1.3.0 — 2026-09-07

**Two plugins can no longer fight over a keyword**

Previously both ran and their results were mixed together with nothing to explain why. A keyword
now has one owner: the plugin installed first keeps it, and any later plugin claiming the same
one is not loaded at all until its `keywords` change.

- Settings shows it as **Keyword taken**, naming the plugin that holds the keyword.
- A tray notification at startup says which plugin is off and why, rather than leaving you to
  discover that typing its keyword does nothing.
- A plugin that loses one keyword does not claim its others either, so a third plugin that only
  clashed with the disabled one stays enabled.
- Reload after editing `plugin.json` re-runs the check.

**The wrong-layout correction was wrong**

The Persian table was taken from the common pictures of a Persian keyboard rather than from
`kbdfa`, the layout Windows actually ships, and it disagreed on nine keys. `پ` is on `\`, not on
`m`; `m` gives `ئ`. So typing `chrome` with the Persian layout on produced `زاقخئث`, which the
app read back as `chrose` and found nothing for. The table now matches what Windows reports for
every key, checked against `VkKeyScanExW` and `ToUnicodeEx` directly.

**New plugin: Keyboard Layout** (`kb`, from the store)

`kb sghl` gives `سلام`. Nothing in it is specific to Persian — the candidate conversions come
from the keyboard layouts installed on your machine, so it works for whatever pair you have.
`kb` on its own converts the clipboard.

## 1.2.0 — 2026-09-07

**An installer, alongside the zip**

Every release now carries two downloads of the same build. The zip is unchanged: extract it
anywhere, run it, delete the folder when you are done. `PlugLauncher-<version>-setup.exe` is for
when you would rather have a Start menu entry and an uninstaller.

- Installs to `%LOCALAPPDATA%\Programs\PlugLauncher`, per user, **no administrator rights**.
- Adds a Start menu shortcut; the desktop shortcut is a checkbox and is off by default.
- Warns, before installing, if the .NET 10 Desktop Runtime is missing, and offers the download
  page. It does not block the install — the runtime can be added afterwards.
- Refuses to overwrite a running PlugLauncher and asks you to close it first, so you cannot end
  up still using the old executable after an upgrade.
- Uninstalling removes the startup entry if it points at the copy being removed, and asks
  separately before deleting your settings, installed plugins and logs.

`SHA256SUMS.txt` now lists both files, and is written without a BOM and with LF endings so
`sha256sum -c SHA256SUMS.txt` works on it.

## 1.1.0 — 2026-09-07

**Start with Windows**

- The app now registers itself under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` so it
  can start when you sign in. `HKCU` and not `HKLM`, so no administrator rights are involved.
- The first time you run it, it asks whether you want that. Whichever you answer, it does not
  ask again — the checkbox in settings is where you change your mind.
- Settings has a **Start with Windows** checkbox. Version 1.0.0 claimed it did, but the setting
  existed only in `settings.json` and nothing ever read or wrote it.
- The checkbox reads the registry rather than `settings.json`, so turning the entry off from
  Task Manager → Startup shows up correctly.
- Moving the PlugLauncher folder used to leave a startup entry pointing at a path that no longer
  exists. The entry is now compared against the running executable and rewritten when it differs.

## 1.0.0 — 2026-09-07

First public release.

**The launcher**

- `Alt+Space` opens a frameless search window. `Enter` runs the selected row, `Tab` completes,
  `↑`/`↓` move, `Esc` closes. Closing the window leaves the app running in the tray.
- An empty query lists your plugins in most-recently-used order.
- Text typed with the wrong keyboard layout is corrected automatically — `زاقخپث` is searched
  as `chrome`, and `Tab` rewrites the box.
- Runs from any folder. No installer, no registry, no administrator rights. Start-with-Windows
  is a checkbox in settings.

**Plugins**

Plugins are `.csx` scripts compiled at startup with Roslyn and cached, so editing one and
restarting is the whole development loop — no build step and no DLLs. A slow or broken plugin
drops out of the results on its own and reports the compile error, with a line number, in
settings.

Three plugins ship with the app:

| Keyword | Plugin | What it does |
|---|---|---|
| *(none)* | Programs | Start menu shortcuts |
| *(none)* | Calculator | Evaluates the query as an expression |
| `pw` | Password Generator | Length, character sets, PIN and hex modes |

**Plugin store**

A store tab in settings installs plugins from a static index on GitHub Pages. Downloads are
checked against the `sha256` in the index, extraction is protected against zip slip, and an
install is staged in a temporary folder so a failed one cannot damage a working plugin.

Four more plugins are there to install:

| Keyword | Plugin | What it does |
|---|---|---|
| `st` | Steam Games | Finds and launches installed games |
| `ssh` | SSH Hosts | Hosts from `~/.ssh/config`, opens a terminal on the one you pick |
| `dev` | Dev Toolbox | uuid, base64, url, hashes, epoch, JSON, JWT, random bytes, slugs |
| `sys` | System | Lock, restart, the msc/cpl consoles, and memory/disk/IP read-outs |

**License**

MIT.
