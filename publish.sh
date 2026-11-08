#!/usr/bin/env bash
# Publie APRS_Live en exécutable autonome (self-contained, single-file) pour
# les trois cibles visées : Linux x64, Windows x64, Windows x86 (32-bit).
#
# Usage : ./publish.sh [linux-x64|win-x64|win-x86|all]
set -euo pipefail

cd "$(dirname "$0")"

RIDS=("linux-x64" "win-x64" "win-x86")
TARGET="${1:-all}"

publish_one() {
    local rid="$1"
    echo "== Publication $rid =="
    dotnet publish src/AprsLive.App -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o "publish/$rid"
}

if [[ "$TARGET" == "all" ]]; then
    for rid in "${RIDS[@]}"; do
        publish_one "$rid"
    done
else
    publish_one "$TARGET"
fi

echo "Terminé. Binaires dans publish/<rid>/."
