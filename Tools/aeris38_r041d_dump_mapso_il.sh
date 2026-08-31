#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <KSP root> [output directory]" >&2
    exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
OUT="${2:-$HOME/Desktop/AERIS_R041D_MapSO_IL_Dump}"
MANAGED="$KSP/KSP_x64_Data/Managed"
ASSEMBLY="$MANAGED/Assembly-CSharp.dll"
SRC="$ROOT/Tools/AERIS38_R041D_dump_mapso_il.cs"
EXE="/tmp/AERIS38_R041D_dump_mapso_il.exe"
TXT="$OUT/mapso_il_dump.txt"
JSON="$OUT/mapso_il_dump.json"
PROVENANCE="$OUT/provenance.txt"
SUMS="$OUT/SHA256SUMS.txt"
BASELINE_HEAD="ed757c9799a5b2c0911e99310907f43281f0b6b5"

[[ -f "$ASSEMBLY" ]] || {
    echo "FAIL: Assembly-CSharp.dll missing: $ASSEMBLY" >&2
    exit 3
}
[[ -f "$SRC" ]] || {
    echo "FAIL: helper source missing: $SRC" >&2
    exit 3
}

for CMD in mono python3 sha256sum git; do
    command -v "$CMD" >/dev/null 2>&1 || {
        echo "FAIL: required command not found: $CMD" >&2
        exit 4
    }
done

COMPILER=""
if command -v mcs >/dev/null 2>&1; then
    COMPILER="mcs"
elif command -v csc >/dev/null 2>&1; then
    COMPILER="csc"
else
    echo "FAIL: neither mcs nor csc found" >&2
    exit 4
fi

mkdir -p "$OUT"
rm -f "$TXT" "$JSON" "$PROVENANCE" "$SUMS" "$EXE"

BRANCH="$(git -C "$ROOT" branch --show-current 2>/dev/null || true)"
HEAD="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || true)"
SOURCE_SHA="$(sha256sum "$SRC" | awk '{print $1}')"
WRAPPER_SHA="$(sha256sum "${BASH_SOURCE[0]}" | awk '{print $1}')"
ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
UTC="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

if [[ "$COMPILER" == "mcs" ]]; then
    mcs -nologo -optimize+ -out:"$EXE" "$SRC"
    COMPILER_VERSION="$(mcs --version 2>/dev/null | head -n 1 || true)"
else
    csc -nologo -optimize+ -out:"$EXE" "$SRC"
    COMPILER_VERSION="$(csc -version 2>/dev/null | head -n 1 || true)"
fi
MONO_VERSION="$(mono --version 2>/dev/null | head -n 1 || true)"

set +e
mono "$EXE" "$MANAGED" 2>&1 | tee "$TXT"
DUMP_RC=${PIPESTATUS[0]}
set -e

cat > "$PROVENANCE" <<EOF
schema=AERIS_R041D_MAPSO_IL_DUMP_PROVENANCE_V1
captured_utc=$UTC
baseline_head=$BASELINE_HEAD
helper_branch=$BRANCH
helper_git_head=$HEAD
helper_source=$SRC
helper_source_sha256=$SOURCE_SHA
wrapper=${BASH_SOURCE[0]}
wrapper_sha256=$WRAPPER_SHA
ksp_root=$KSP
managed_dir=$MANAGED
assembly=$ASSEMBLY
assembly_sha256=$ASSEMBLY_SHA
compiler=$COMPILER
compiler_version=$COMPILER_VERSION
mono_version=$MONO_VERSION
dump_exit_code=$DUMP_RC
EOF

python3 - "$TXT" "$JSON" "$KSP" "$MANAGED" "$ASSEMBLY" "$BRANCH" "$HEAD" "$BASELINE_HEAD" "$SOURCE_SHA" "$WRAPPER_SHA" "$ASSEMBLY_SHA" "$DUMP_RC" "$UTC" <<'PY'
import hashlib
import json
import pathlib
import sys

(
    txt_name,
    json_name,
    ksp_root,
    managed_dir,
    assembly_path,
    helper_branch,
    helper_head,
    baseline_head,
    source_sha,
    wrapper_sha,
    assembly_sha,
    dump_rc,
    captured_utc,
) = sys.argv[1:]

txt_path = pathlib.Path(txt_name)
raw = txt_path.read_text(encoding="utf-8", errors="replace")
lines = raw.splitlines()

def first_value(key):
    prefix = key + "="
    for line in lines:
        if line.startswith(prefix):
            return line[len(prefix):]
    return None

def as_bool(value):
    if value is None:
        return None
    return value.strip().lower() == "true"

def as_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None

methods = []
parts = raw.split("--- METHOD ---")
for block in parts[1:]:
    method = {"instructions": [], "locals_detail": [], "exception_handlers_detail": []}
    for line in block.splitlines():
        line = line.strip()
        if not line:
            continue
        if line.startswith("IL "):
            method["instructions"].append(line)
            continue
        if line.startswith("LOCAL "):
            method["locals_detail"].append(line)
            continue
        if line.startswith("EH "):
            method["exception_handlers_detail"].append(line)
            continue
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key in {
            "signature", "declaring_type", "metadata_token", "attributes", "impl_flags",
            "managed_body", "il_bytes", "il_hex", "il_sha256", "max_stack",
            "init_locals", "locals", "exception_handlers"
        }:
            if key == "locals":
                method["local_count"] = as_int(value)
            elif key == "exception_handlers":
                method["exception_handler_count"] = as_int(value)
            elif key in {"il_bytes", "max_stack"}:
                method[key] = as_int(value)
            elif key in {"managed_body", "init_locals"}:
                method[key] = as_bool(value)
            else:
                method[key] = value
    if "signature" in method:
        methods.append(method)

payload = {
    "schema": "AERIS_R041D_MAPSO_IL_DUMP_V1",
    "captured_utc": captured_utc,
    "baseline_head": baseline_head,
    "helper_branch": helper_branch,
    "helper_git_head": helper_head,
    "helper_source_sha256": source_sha,
    "wrapper_sha256": wrapper_sha,
    "ksp_root": ksp_root,
    "managed_dir": managed_dir,
    "assembly": assembly_path,
    "assembly_sha256": assembly_sha,
    "assembly_full_name": first_value("assembly_full_name"),
    "module_name": first_value("module_name"),
    "module_mvid": first_value("module_mvid"),
    "type": first_value("type"),
    "method_count": as_int(first_value("method_count")),
    "required": {
        "GetPixelFloat": as_bool(first_value("required_GetPixelFloat")),
        "GetPixelColor32": as_bool(first_value("required_GetPixelColor32")),
        "GetPixelColor": as_bool(first_value("required_GetPixelColor")),
    },
    "helper_result": first_value("AERIS38_R041D_MAPSO_IL_DUMP"),
    "dump_exit_code": as_int(dump_rc),
    "text_sha256": hashlib.sha256(raw.encode("utf-8")).hexdigest(),
    "methods": methods,
}

pathlib.Path(json_name).write_text(
    json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

(
    cd "$OUT"
    sha256sum mapso_il_dump.txt mapso_il_dump.json provenance.txt > SHA256SUMS.txt
)

echo
echo "=== AERIS R041D MAPSO-1 ARTIFACTS ==="
echo "output=$OUT"
echo "dump_exit_code=$DUMP_RC"
grep -E '^(module_mvid|required_GetPixelFloat|required_GetPixelColor32|required_GetPixelColor|AERIS38_R041D_MAPSO_IL_DUMP)=' "$TXT" || true
echo
echo "=== SHA256 ==="
cat "$SUMS"

if [[ $DUMP_RC -ne 0 ]]; then
    echo "AERIS_R041D_MAPSO1=FAIL" >&2
    exit "$DUMP_RC"
fi

for TARGET in GetPixelFloat GetPixelColor32 GetPixelColor; do
    grep -q "^required_${TARGET}=true$" "$TXT" || {
        echo "FAIL: required target not captured: $TARGET" >&2
        exit 5
    }
done

grep -q '^module_mvid=.' "$TXT" || {
    echo "FAIL: module MVID not captured" >&2
    exit 6
}

echo "AERIS_R041D_MAPSO1=PASS"
