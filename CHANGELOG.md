# Changelog

The release workflow reads the section matching the tag out of this file and uses it as the
release notes, so every version needs a `## <version>` heading here before it can be tagged.

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
