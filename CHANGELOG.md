# Changelog

The release workflow reads the section matching the tag out of this file and uses it as the
release notes, so every version needs a `## <version>` heading here before it can be tagged.

## 1.5.0 — 2026-09-08

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
