#!/usr/bin/env bash
set -e

# ============================================================
# Flowery.Uno Gallery — Pacchetti .deb per Linux (x64 e arm64), net10.0-desktop
#
# Prerequisiti (una volta sola):
#   brew install dpkg
#
# Il lavoro vero lo fa packaging/linux/build-deb.sh.
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUT_DIR="$SCRIPT_DIR/output/installer"

if ! command -v dpkg-deb &>/dev/null; then
    echo "[ERRORE] dpkg-deb non trovato. Installa con:"
    echo "  brew install dpkg"
    exit 1
fi

echo "Pulizia output precedente..."
mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR"/flowery-uno-gallery_*.deb

for rid in linux-x64 linux-arm64; do
    "$SCRIPT_DIR/packaging/linux/build-deb.sh" "$rid" "$OUT_DIR"
done

ls -lh "$OUT_DIR"/flowery-uno-gallery_*.deb
