#!/usr/bin/env bash
# Builds the Windows release: dist/EmpyrionAdminManager-<version>/ and its zip.
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-dotnet}"
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
NAME="EmpyrionAdminManager-$VERSION"
rm -rf dist
mkdir -p dist
COMMON=(-c Release -r win-x64 --self-contained true -p:DebugType=none -o "dist/$NAME")
"$DOTNET" publish src/Eam.Engine/Eam.Engine.csproj "${COMMON[@]}"
"$DOTNET" publish src/Eam.Desktop/Eam.Desktop.csproj "${COMMON[@]}"
cp -r packaging/. "dist/$NAME/"
cp LICENSE "dist/$NAME/LICENSE.txt"
cp CHANGELOG.md "dist/$NAME/CHANGELOG.md"
find dist -name '*.pdb' -delete
(cd dist && zip -qr "$NAME.zip" "$NAME")
echo "Built dist/$NAME.zip"
