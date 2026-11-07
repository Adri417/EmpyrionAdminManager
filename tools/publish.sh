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
# EAM game mod, copied into each server by the engine (Content\Mods\EAM).
"$DOTNET" build src/Eam.Mod/Eam.Mod.csproj -c Release -o "dist/mod-build"
mkdir -p "dist/$NAME/mod/EAM"
cp dist/mod-build/EamMod.dll dist/mod-build/EAM_Info.yaml "dist/$NAME/mod/EAM/"
cp -r packaging/. "dist/$NAME/"
cp LICENSE "dist/$NAME/LICENSE.txt"
cp CHANGELOG.md "dist/$NAME/CHANGELOG.md"
find dist -name '*.pdb' -delete
(cd dist && zip -qr "$NAME.zip" "$NAME")
echo "Mod: dist/$NAME/mod/EAM"
echo "Built dist/$NAME.zip"
