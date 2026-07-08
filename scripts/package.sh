#!/usr/bin/env bash
# Build the Mycelium Yak package into dist/.
#
# Usage: scripts/package.sh
#
# Requires the .NET SDK. Uses the yak CLI from Rhino 8 if installed
# (macOS: /Applications/Rhino 8.app, Windows: C:\Program Files\Rhino 8),
# otherwise leaves the assembled dist/ folder for manual packaging.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO_ROOT/src/Mycelium/bin/Release/net7.0-windows"
DIST="$REPO_ROOT/dist"

# Version source of truth is the root manifest.yml (4-part X.Y.Z.W, same as CI).
VERSION=$(sed -n 's/^version:[[:space:]]*\(.*\)$/\1/p' "$REPO_ROOT/manifest.yml" | head -1)
ASSEMBLY_VERSION=$(echo "$VERSION" | cut -d. -f1-3)
echo "Packaging Mycelium $VERSION (assembly $ASSEMBLY_VERSION)"

dotnet build "$REPO_ROOT/Mycelium.sln" -c Release -p:Version="$ASSEMBLY_VERSION"

rm -rf "$DIST"
mkdir -p "$DIST"

cp "$OUT/Mycelium.gha" "$DIST/"
cp -R "$REPO_ROOT/src/Mycelium/Templates" "$DIST/Templates"
cp "$REPO_ROOT/docs/images/logo.png" "$DIST/icon.png"
cp "$REPO_ROOT/manifest.yml" "$DIST/manifest.yml"

# Find a yak CLI
YAK=""
if [ -x "/Applications/Rhino 8.app/Contents/Resources/bin/yak" ]; then
  YAK="/Applications/Rhino 8.app/Contents/Resources/bin/yak"
elif command -v yak >/dev/null 2>&1; then
  YAK="yak"
fi

if [ -z "$YAK" ]; then
  echo "yak CLI not found. dist/ is assembled; run 'yak build' inside it manually."
  exit 0
fi

(cd "$DIST" && "$YAK" build)

echo
echo "Package ready:"
ls -la "$DIST"/*.yak
echo
echo "Publishing happens via CI: push to the pre-release or release branch."
echo "Manual fallback: cd dist && \"$YAK\" login && \"$YAK\" push <package>.yak"
