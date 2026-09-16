#!/usr/bin/env bash
set -euo pipefail
# Run on a Mac after the interactive release checklist has passed.
# Credentials remain in the maintainer's keychain; no private keys enter this repo.
: "${RACKS_PUBLIC_KEY:?Set the Ed25519 public key}"
: "${RACKS_FEED_BASE:?Set the HTTPS release feed base}"
: "${RACKS_CODESIGN_IDENTITY:?Set the Developer ID Application identity}"
: "${RACKS_NOTARY_PROFILE:?Set an existing notarytool keychain profile}"
[[ "$RACKS_FEED_BASE" == https://* ]] || { echo 'The feed must use HTTPS.' >&2; exit 1; }
[[ "$(uname -s)" == Darwin ]] || { echo 'Run this script on a Mac.' >&2; exit 1; }
cd "$(dirname "$0")/.."
dotnet build Racks.Overhaul.sln -c Release --warnaserror
dotnet test tests/Racks.Core.Tests -c Release --no-build
pwsh -File scripts/Test-Desktop.ps1 -Configuration Release
output="$PWD/.artifacts/mac-package-$(date +%Y%m%d%H%M%S)"
app="$output/Racks.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish src/Racks.Desktop/Racks.Desktop.csproj -c Release -r osx-arm64 --self-contained true -o "$app/Contents/MacOS" \
  "-p:RacksUpdatePublicKey=$RACKS_PUBLIC_KEY" "-p:RacksUpdateFeedBase=$RACKS_FEED_BASE" --warnaserror
cp installer/mac/Info.plist "$app/Contents/Info.plist"
while IFS= read -r -d '' binary; do
  if file "$binary" | grep -q 'Mach-O'; then
    codesign --force --timestamp --options runtime --entitlements installer/mac/entitlements.plist --sign "$RACKS_CODESIGN_IDENTITY" "$binary"
  fi
done < <(find "$app/Contents/MacOS" -type f -print0)
codesign --force --timestamp --options runtime --entitlements installer/mac/entitlements.plist --sign "$RACKS_CODESIGN_IDENTITY" "$app"
codesign --verify --deep --strict "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/notarization.zip"
xcrun notarytool submit "$output/notarization.zip" --keychain-profile "$RACKS_NOTARY_PROFILE" --wait
xcrun stapler staple "$app"
xcrun stapler validate "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/Racks-2.0.0-beta.1-osx-arm64.zip"
echo "Built and notarized $output/Racks-2.0.0-beta.1-osx-arm64.zip. Sign its appcast/package with NetSparkle before distribution. Nothing was uploaded to GitHub."
