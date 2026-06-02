#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"

if [[ -z "${VINTAGE_STORY:-}" ]]; then
  echo "VINTAGE_STORY env var not set. Run: export VINTAGE_STORY=\"/path/to/Vintagestory\""
  exit 1
fi

modinfo="$root/modinfo.json"
version="$(python3 -c 'import json, pathlib; print(json.loads(pathlib.Path("'"$modinfo"'").read_text())["version"])')"

build_stamp="$(date -u '+%Y-%m-%d %H:%M:%S UTC%z')"
short_sha="$(git -C "$root" rev-parse --short HEAD 2>/dev/null || echo unknown)"

cat > "$root/src/BuildInfo.cs" <<EOF
namespace AxleChisel
{
    public static class BuildInfo
    {
        public const string Version = "$version";
        public const string Stamp = "$build_stamp";
        public const string Sha = "$short_sha";
    }
}
EOF

echo "Building AxleChisel.dll (Release)..."
dotnet build -c Release "$root/AxleChisel.csproj"

dll="$root/bin/Release/AxleChisel.dll"
if [[ ! -f "$dll" ]]; then
  echo "Build output not found at $dll"
  exit 1
fi

stage="$root/dist/stage"
dist="$root/dist"
zipfile="$dist/AxleChisel-$version.zip"

rm -rf "$stage"
mkdir -p "$stage"

cp "$dll" "$stage/AxleChisel.dll"
cp "$root/modinfo.json" "$stage/modinfo.json"
rm -rf "$stage/assets"
cp -R "$root/assets" "$stage/assets"

mkdir -p "$dist"
if [[ -f "$zipfile" ]]; then
  rm -f "$zipfile"
fi

cd "$stage"
zip -r "$zipfile" ./*

echo "Done: $zipfile"
