# The PlugLauncher plugin store

The store is a **static folder** on GitHub Pages: one `index.json` and the package files next to
it. No server, no database, no publishing key. The client makes exactly two HTTPS `GET`s.

| What | Value |
|---|---|
| Store URL | `https://mul83rry.github.io/PlugLauncher/` |
| Index | `https://mul83rry.github.io/PlugLauncher/index.json` |
| Source | the `gh-pages` branch (root) of this repository |
| Local worktree | `../PlugLauncher-pages` |

**Nothing is required of the user**: no git, no account, no token.

## The package format (`.plz`)

An ordinary zip with this layout (the extension is only for recognition):

```
plugin.json      # required, at the root of the package
main.csx         # the entry file; its name comes from the manifest's entry field
assets/          # optional — icons and static files
screenshots/     # optional — detail-page screenshots (png/jpg/jpeg, flat, ordered by file name)
theme/           # optional — styles specific to the plugin
```

Screenshots are a convention, not a manifest field: every flat image file inside `screenshots/` is
pulled out when the store is built, served under `packages/{id}/{version}/screenshots/`, and
listed in the index's `screenshots` field ordered by file name. The launcher shows them on the
package detail page; a package without any simply does not get that section.

`plugin.json` must carry: `id` (3 to 100 characters, only letters/digits/dot/dash), `name`,
`version` in the `1.0.0` or `1.0.0-beta` shape, and an `entry` whose file really is inside the
package. If `icon` is set, that file must be in the package too.

Two more optional fields are looked at by both the store and the launcher: `platforms`
(`windows`, `macos`, `linux` — empty means everywhere) and `minCore` (the oldest launcher the
plugin works on). A package that does not fit this system or this version is not installed at
all, and the reason is written on the store row itself.

## Building and publishing a package

```powershell
# build a .plz file from a plugin folder
./tools/pack-plugin.ps1 -Path ./plugins/steam-games -Output ./dist

# publish — one command, no key and no server
./tools/publish-plugin.ps1 -File ./dist/com.pluglauncher.steam-games-1.0.0.plz
```

`publish-plugin.ps1` does these in a row: puts the package in `./dist`, calls
`build-static-store.ps1`, mirrors the output into the `../PlugLauncher-pages` worktree (branch
`gh-pages`) and commits + pushes. If the worktree is missing it creates it. `-NoPush` commits
without pushing, `-Message` changes the commit message.

Because the whole `dist` folder is re-read every time, **deleting** a package is also just
removing its `.plz` from `dist` and running the same script again.

## Building the static output

```powershell
# turns every .plz present into one static store
# (publish-plugin.ps1 calls this itself; this command is for when you only want to see the output)
./tools/build-static-store.ps1 -Packages ./dist -Output ./dist/store-static -Clean
```

Output:

```
index.json                                   # the list, latest version of each package
packages/{id}/{version}/{id}-{version}.plz   # every version (old links keep working)
packages/{id}/{version}/icon.png             # the icon, pulled out of the package
packages/{id}/{version}/screenshots/*.jpg    # the screenshots, from inside the package too
```

`index.json` has the `{ total, items }` shape and its `downloadUrl`/`iconUrl` are **relative**, so
it works under any prefix — including the Pages subfolder path. `sha256` and `size` are computed
from the file itself.

`.nojekyll` at the root of `gh-pages` is required so Pages skips Jekyll processing; without it
any file or folder starting with `_` is not served.

### The Content-Type note

`StoreClient` deliberately does not use `GetFromJsonAsync`: it reads the body as a string and
deserializes it itself. The reason is that static hosts do not always send `application/json` —
`raw.githubusercontent.com` sends `text/plain` even for JSON files, and that method rejects the
response. With this change, any host that returns the file works. (GitHub Pages itself sends
`application/json`, but that assumption is not hardcoded anywhere.)

## Package validation

Because there is no server, the validation gate at **publish** time is only `pack-plugin.ps1`:
the existence of `plugin.json`, the presence of `id` and `version`, the entry file really being
in the folder, and sane `platforms` and `minCore` values — one wrong name in `platforms` means
the plugin loads on no system at all and nothing anywhere says why. The `bin`, `obj`, `.git`
and `*.user` files are also left out of the package.

The **client**-side checks are untouched, and the important ones live there:

- the downloaded file's `sha256` is compared with the value announced in `index.json`; a mismatch
  stops the install and no file is written.
- opening the zip is protected against **zip slip** — every entry must stay inside the target
  folder.
- installing happens into a `.installing` folder and is swapped in at the end, so a half-finished
  install never lands on the previous good version. A package without `plugin.json` is rejected.

## Client security

The store URL is not editable in the UI: the default is hardcoded in `AppSettings.DefaultStoreUrl`
and changing it takes a manual edit of `%APPDATA%\PlugLauncher\settings.json`. When package code
runs without a sandbox, swapping the source of packages should not be one textbox away.

Old installs that had the previous store URL saved in `settings.json` are moved to the Pages URL
by a one-shot migration in `SettingsStore.Load` (only when the value is **exactly**
`AppSettings.LegacyStoreUrl`; any other value is left alone).

**The remaining security note**: plugin code runs with the user's full access (no sandbox).
`sha256` only guarantees the file is what the store has; it does not guarantee its content is
harmless. As long as publishing happens only from your repository, this risk is contained.

## The store window in the client

The client-side store is its own window (`StoreWindow`), opened by the **Plugin store** button in
settings — it is no longer a separate tab. The layout follows the Windows 11 store: a left
navigation rail (Home / Browse / Library), a banner for the newest package by `publishedAt`,
horizontal rows, a card grid in Browse, a detail page per package, and a library of installed
ones with an updates section. Search is live and filtered client-side (the full index comes
down once), and installing comes with a download progress bar — `StoreClient.InstallAsync` takes
an `IProgress<DownloadProgress>` and gets the download length from the response header or the
index's `size`.

All of this works with the current `index.json`; no new required fields were added. The optional
fields the UI uses: `publishedAt` (banner and dates), `size` (download progress),
`platforms`/`minCore` (the Unavailable button and its reason on the detail page), `iconUrl` and
`screenshots` (the card icon and the detail page's gallery — images are downloaded after the
page opens, one by one and only once per package).
