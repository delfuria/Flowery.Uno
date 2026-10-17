#!/usr/bin/env bash
# Rende app_icon.svg del Gallery in un PNG quadrato (solo macOS: qlmanage + sips).
#
#   packaging/render-icon.sh <size> <output.png>
set -euo pipefail

SIZE="${1:?uso: $0 <size> <output.png>}"
OUT="${2:?uso: $0 <size> <output.png>}"
PROJECT_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
SVG="$PROJECT_ROOT/Flowery.Uno.Gallery.Core/Assets/app_icon.svg"

TMP="$(mktemp -d "${TMPDIR:-/tmp}/flowery-icon.XXXXXX")"
trap 'rm -r "$TMP"' EXIT

qlmanage -t -s 1024 -o "$TMP" "$SVG" >/dev/null 2>&1
mkdir -p "$(dirname "$OUT")"
sips -z "$SIZE" "$SIZE" "$TMP/app_icon.svg.png" --out "$OUT" >/dev/null
