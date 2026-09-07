# Changelog

The release workflow reads the section matching the tag out of this file and uses it as the
release notes, so every version needs a `## <version>` heading here before it can be tagged.

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
