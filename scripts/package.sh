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
PROJECT="$REPO_ROOT/src/Mycelium/Mycelium.csproj"
OUT="$REPO_ROOT/src/Mycelium/bin/Release/net7.0-windows"
DIST="$REPO_ROOT/dist"

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$PROJECT")
echo "Packaging Mycelium $VERSION"

dotnet build "$REPO_ROOT/Mycelium.sln" -c Release

rm -rf "$DIST"
mkdir -p "$DIST"

cp "$OUT/Mycelium.gha" "$DIST/"
cp -R "$REPO_ROOT/src/Mycelium/Templates" "$DIST/Templates"
cp "$REPO_ROOT/docs/images/logo.png" "$DIST/icon.png"
sed "s/^version: .*/version: $VERSION/" "$REPO_ROOT/src/Mycelium/manifest.yml" > "$DIST/manifest.yml"

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
echo "To publish (requires Rhino account login):"
echo "  cd dist && \"$YAK\" login && \"$YAK\" push mycelium-$VERSION-rh8_0-any.yak"
