#!/usr/bin/env bash
#
# Builds PlugLauncher.app on a Mac.
#
# The bundle is the point, not a nicety. RegisterEventHotKey needs the process to be a real
# application as far as the window server is concerned, and a bare binary started from a
# terminal is not one — so `dotnet run` can leave you with a launcher that never opens.
#
# Self-contained on purpose. The Windows release is framework-dependent because the README
# tells people which runtime to install; here nobody has read anything yet, and an app that
# silently refuses to start because it cannot find a runtime is the worst way to spend a
# first test.
#
# Unsigned, so it only runs on the machine that built it. macOS does not quarantine what was
# built locally; copy the .app to another Mac and Gatekeeper will refuse it.
#
#   ./tools/build-mac.sh              -> publish/PlugLauncher.app
#   ./tools/build-mac.sh -o /tmp/out  -> /tmp/out/PlugLauncher.app

set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="$repo/publish"

while [ $# -gt 0 ]; do
  case "$1" in
    -o|--output) output="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [ "$(uname -s)" != "Darwin" ]; then
  echo "This builds a macOS bundle and only works on a Mac." >&2
  exit 1
fi

case "$(uname -m)" in
  arm64) rid="osx-arm64" ;;
  x86_64) rid="osx-x64" ;;
  *) echo "unknown architecture: $(uname -m)" >&2; exit 1 ;;
esac

# Same single source of truth the Windows build reads, so both stamp the same number
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$repo/Directory.Build.props" | head -1)"
[ -n "$version" ] || { echo "Directory.Build.props has no <Version>." >&2; exit 1; }

app="$output/PlugLauncher.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

echo "Publishing $version for $rid..."

dotnet publish "$repo/src/PlugLauncher.App/PlugLauncher.App.csproj" \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -p:Version="$version" \
  -o "$app/Contents/MacOS" \
  --nologo

# The bundled plugins are the reason anyone would run this at all, so a bundle without them
# is a failure, not a smaller success.
plugins="$(find "$app/Contents/MacOS/plugins" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l | tr -d ' ')"
[ "$plugins" != "0" ] || { echo "No plugins were copied into the bundle." >&2; exit 1; }

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>PlugLauncher</string>
    <key>CFBundleDisplayName</key><string>PlugLauncher</string>
    <key>CFBundleIdentifier</key><string>com.pluglauncher.app</string>
    <key>CFBundleExecutable</key><string>PlugLauncher</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
</dict>
</plist>
PLIST

# No LSUIElement yet. A launcher belongs in the menu bar and not in the Dock, but while the
# hotkey is still unproven the Dock icon is the only way left to quit the thing.

chmod +x "$app/Contents/MacOS/PlugLauncher"

files="$(find "$app" -type f | wc -l | tr -d ' ')"
size="$(du -sh "$app" | cut -f1)"

echo
echo "Version : $version ($rid)"
echo "Payload : $files files, $plugins bundled plugins, $size"
echo
echo "  $app"
echo
echo "Run it with:  open \"$app\""
