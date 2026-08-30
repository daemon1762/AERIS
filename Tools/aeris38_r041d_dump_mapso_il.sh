#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "usage: $0 <KSP root>" >&2
    exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
MANAGED="$KSP/KSP_x64_Data/Managed"
SRC="$ROOT/Tools/AERIS38_R041D_dump_mapso_il.cs"
EXE="/tmp/AERIS38_R041D_dump_mapso_il.exe"

[[ -f "$MANAGED/Assembly-CSharp.dll" ]] || {
    echo "FAIL: Assembly-CSharp.dll missing: $MANAGED/Assembly-CSharp.dll" >&2
    exit 3
}

COMPILER=""
if command -v mcs >/dev/null 2>&1; then
    COMPILER="mcs"
elif command -v csc >/dev/null 2>&1; then
    COMPILER="csc"
else
    echo "FAIL: neither mcs nor csc found" >&2
    exit 4
fi

rm -f "$EXE"
if [[ "$COMPILER" == "mcs" ]]; then
    mcs -nologo -optimize+ -out:"$EXE" "$SRC"
else
    csc -nologo -optimize+ -out:"$EXE" "$SRC"
fi

mono "$EXE" "$MANAGED"
