#!/usr/bin/env bash
set -e

# ============================================================
# Flowery.Uno Gallery — Build DMG macOS (arm64 + x64), distribuzione diretta
#
# Il Gallery non ha un TFM maccatalyst: si usa net10.0-desktop (Skia) pubblicato
# per osx-arm64 e osx-x64; un unico .app contiene entrambi i payload.
#
# Prerequisiti:
#   brew install create-dmg
#
# Certificato necessario nel Keychain:
#   "Developer ID Application: Stefano Del Furia (VZWMBQL256)"
# ============================================================

DEV_ID_CERT="Developer ID Application: Stefano Del Furia (VZWMBQL256)"

# Notarizzazione disabilitata come per RadioE45 (vedi note lì).
NOTARIZE=false
NOTARY_PROFILE="flowery-gallery-notary"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="$PROJECT_ROOT/Flowery.Uno.Gallery/Flowery.Uno.Gallery.csproj"
TFM=net10.0-desktop
APP_NAME="Flowery.Uno Gallery"
EXE=Flowery.Uno.Gallery
OUT_DIR="$SCRIPT_DIR/output/installer"
PACKAGING_DIR="$SCRIPT_DIR/packaging"
ENTITLEMENTS="$PACKAGING_DIR/macos/Entitlements.plist"
FINAL_DMG="$OUT_DIR/FloweryUnoGallery.dmg"

# /tmp invece di OneDrive: OneDrive ri-aggiunge xattr propri dopo xattr -cr,
# rompendo la verifica firma di codesign
WORK_DIR="/tmp/FloweryGallery-dmg-build"
APP="$WORK_DIR/$APP_NAME.app"
DMG_STAGING="$WORK_DIR/dmg-staging"

is_macho() { file -b "$1" | grep -q "Mach-O"; }

echo "========================================"
echo " Flowery.Uno Gallery — DMG (distribuzione diretta)"
echo "========================================"

# --- Verifica prerequisiti ---
if ! command -v create-dmg &>/dev/null; then
    echo "[ERRORE] create-dmg non trovato. Installa con:"
    echo "  brew install create-dmg"
    exit 1
fi

if ! security find-identity -v -p codesigning | grep -q "$DEV_ID_CERT"; then
    echo "[ERRORE] Certificato Developer ID non trovato nel Keychain:"
    echo "  $DEV_ID_CERT"
    exit 1
fi

# --- Pulizia ---
echo ""
echo "[1/6] Pulizia output precedente..."
rm -r "$WORK_DIR" 2>/dev/null || true
mkdir -p "$WORK_DIR" "$OUT_DIR"

dotnet restore "$PROJECT" -nologo -v quiet
VERSION="$(dotnet msbuild "$PROJECT" -nologo -getProperty:Version -p:TargetFramework=$TFM | tr -d '[:space:]')"
echo "Versione rilevata: $VERSION"

# --- Build ---
echo ""
echo "[2/6] dotnet publish (Release, $TFM, osx-arm64 + osx-x64)..."
for rid in osx-arm64 osx-x64; do
    dotnet publish "$PROJECT" -f $TFM -c Release -r "$rid" --self-contained \
        -p:UseMonoRuntime=false -p:DebugType=None -p:DebugSymbols=false -o "$WORK_DIR/$rid" -nologo
done

# --- Bundle .app con due payload (arm64 + x64) ---
echo ""
echo "[3/6] Creazione .app (payload arm64 + x64, launcher che sceglie)..."
# Niente lipo: le DLL gestite del framework (System.Private.CoreLib, ...) sono ReadyToRun
# e specifiche per architettura, quindi un apphost universale con DLL arm64 fallisce sotto
# Rosetta ("Failed to load System.Private.CoreLib.dll ... 0x8007000B").
# Payload in Contents/Resources: le cartelle satellite (zh-Hans, ...) dentro Contents/MacOS
# fanno fallire codesign ("code object is not signed at all").
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
ditto "$WORK_DIR/osx-arm64" "$APP/Contents/Resources/app-arm64"
ditto "$WORK_DIR/osx-x64" "$APP/Contents/Resources/app-x64"
cat > "$APP/Contents/MacOS/$EXE" <<'LAUNCHER'
#!/bin/sh
# Sceglie il payload dall'hardware reale (hw.optional.arm64 resta 1 anche sotto Rosetta).
DIR="$(cd "$(dirname "$0")/../Resources" && pwd)"
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = "1" ]; then
    exec /usr/bin/arch -arm64 "$DIR/app-arm64/Flowery.Uno.Gallery" "$@"
fi
exec "$DIR/app-x64/Flowery.Uno.Gallery" "$@"
LAUNCHER
chmod +x "$APP/Contents/MacOS/$EXE"

sed "s/@VERSION@/$VERSION/g" "$PACKAGING_DIR/macos/Info.plist.in" > "$APP/Contents/Info.plist"

# Icona: PNG renderizzati dal SVG del Gallery -> .icns
ICONSET="$WORK_DIR/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
    "$PACKAGING_DIR/render-icon.sh" "$size" "$ICONSET/icon_${size}x${size}.png"
    "$PACKAGING_DIR/render-icon.sh" "$((size * 2))" "$ICONSET/icon_${size}x${size}@2x.png"
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# --- Firma .app con Developer ID ---
echo ""
echo "[4/6] Firma .app con Developer ID Application..."
xattr -cr "$APP"

# Prima tutti i Mach-O interni (dylib, so, eseguibili), poi il bundle.
find "$APP/Contents" -type f | sort | while read -r f; do
    if is_macho "$f"; then
        codesign --sign "$DEV_ID_CERT" --force --options runtime --timestamp \
            --entitlements "$ENTITLEMENTS" "$f"
    fi
done

codesign \
    --sign "$DEV_ID_CERT" \
    --force \
    --options runtime \
    --timestamp \
    --entitlements "$ENTITLEMENTS" \
    "$APP"

echo "Verifica firma..."
codesign --verify --deep --strict "$APP"
spctl --assess --type execute "$APP" 2>&1 || true

# --- Creazione DMG ---
echo ""
echo "[5/6] Creazione DMG con create-dmg..."
rm -f "$FINAL_DMG"

# Stacca eventuali volumi dmg temporanei residui da run precedenti falliti
for stale_vol in /Volumes/dmg.*; do
    [ -d "$stale_vol" ] && hdiutil detach "$stale_vol" -force 2>/dev/null || true
done
rm -f "$OUT_DIR"/rw.*.FloweryUnoGallery.dmg 2>/dev/null || true

mkdir -p "$DMG_STAGING"
# ditto e non "cp -r": preserva symlink/xattr senza rompere la firma.
ditto "$APP" "$DMG_STAGING/$APP_NAME.app"

create-dmg \
    --volname "$APP_NAME" \
    --window-size 600 420 \
    --icon-size 128 \
    --icon "$APP_NAME.app" 160 200 \
    --app-drop-link 440 200 \
    --no-internet-enable \
    "$FINAL_DMG" \
    "$DMG_STAGING"

codesign --sign "$DEV_ID_CERT" --timestamp "$FINAL_DMG"
echo "DMG firmato."

# --- Notarizzazione ---
if [ "$NOTARIZE" = "true" ]; then
    echo ""
    echo "[6/6] Notarizzazione DMG (può richiedere qualche minuto)..."
    xcrun notarytool submit "$FINAL_DMG" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$FINAL_DMG"
    echo "Notarizzazione completata."
else
    echo "[6/6] Notarizzazione saltata (NOTARIZE=false)."
fi

# --- Output finale ---
echo ""
echo "========================================"
echo " DMG pronto:"
echo " $FINAL_DMG"
echo "========================================"
