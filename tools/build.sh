#!/usr/bin/env bash
# Builds SchoolShield-Setup-<version>.exe and schoolshield-teacher_<version>_all.deb (and their signatures) on macOS / Linux / Windows (Git Bash).
#
#   tools/build.sh            build + sign (if the signing key exists)
#   tools/build.sh --no-sign  build only
#
# Needs: .NET SDK 6+ (targets .NET Framework 4.8 through reference assemblies), zip, openssl (for signing), python3 (Linux package).
# The private signing key is NEVER in the repository: ~/.schoolshield/signing-key.pem (override with SS_SIGNING_KEY).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/SchoolShield/SchoolShield.csproj | head -1)"
DIST="$ROOT/dist"
KEY="${SS_SIGNING_KEY:-$HOME/.schoolshield/signing-key.pem}"
SIGN=1
[ "${1:-}" = "--no-sign" ] && SIGN=0

echo "== SchoolShield $VERSION =="
rm -rf "$DIST"
mkdir -p "$DIST/payload"

echo "-- catalog check"
python3 tools/check-catalog.py

echo "-- launcher"
dotnet build src/SchoolShield -c Release -o "$DIST/app" -nologo -v q
echo "-- payload"
cp "$DIST"/app/*.exe "$DIST"/app/*.exe.config "$DIST"/app/*.dll "$DIST/payload/"
[ -d "$DIST/app/runtimes" ] && cp -R "$DIST/app/runtimes" "$DIST/payload/runtimes"   # WebView2Loader.dll must stay in runtimes/win-x64/native
mkdir -p "$DIST/payload/scripts" "$DIST/payload/config" "$DIST/payload/assets"
cp scripts/*.ps1 "$DIST/payload/scripts/"
cp config/* "$DIST/payload/config/"
cp assets/schoolshield.ico "$DIST/payload/assets/"
cp LICENSE THIRD_PARTY_NOTICES.txt README.md "$DIST/payload/"
(cd "$DIST/payload" && zip -qr "$DIST/payload.zip" .)

echo "-- installer"
dotnet build src/Setup -c Release -o "$DIST/setup" -nologo -v q -p:PayloadZip="$DIST/payload.zip"
OUT="$DIST/SchoolShield-Setup-$VERSION.exe"
cp "$DIST/setup/SchoolShield-Setup.exe" "$OUT"

if [ "$SIGN" = 1 ]; then
  if [ -f "$KEY" ]; then
    openssl dgst -sha256 -sign "$KEY" -out "$OUT.sig" "$OUT"
    echo "-- signed: $(basename "$OUT").sig"
  else
    echo "!! signing key not found ($KEY): update signature NOT created" >&2
  fi
fi
( cd "$DIST" && shasum -a 256 "$(basename "$OUT")" > "$(basename "$OUT").sha256" )

echo "-- linux tests"
python3 -W ignore -I -m unittest discover -s linux/tests
echo "-- linux package (Linux Mint, teacher edition)"
python3 linux/packaging/build_deb.py --out "$DIST"
DEB="$(ls "$DIST"/schoolshield-teacher_*_all.deb)"
if [ "$SIGN" = 1 ] && [ -f "$KEY" ]; then
  openssl dgst -sha256 -sign "$KEY" -out "$DEB.sig" "$DEB"
  echo "-- signed: $(basename "$DEB").sig"
fi
( cd "$DIST" && shasum -a 256 "$(basename "$DEB")" > "$(basename "$DEB").sha256" )

echo
ls -la "$DIST"/SchoolShield-Setup-* "$DIST"/schoolshield-teacher_*
echo "OK"
