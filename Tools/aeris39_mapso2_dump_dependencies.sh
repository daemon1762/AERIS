#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <KSP root> [output directory]" >&2
    exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
OUT="${2:-$HOME/Desktop/AERIS39_MAPSO2A_Dependency_Dump}"
MANAGED="$KSP/KSP_x64_Data/Managed"
ASSEMBLY="$MANAGED/Assembly-CSharp.dll"
SRC="$ROOT/Tools/AERIS39_MAPSO2_dump_dependencies.cs"
EXE="/tmp/AERIS39_MAPSO2_dump_dependencies.exe"
TXT="$OUT/mapso2a_dependency_dump.txt"
JSON="$OUT/mapso2a_dependency_dump.json"
PROVENANCE="$OUT/provenance.txt"
SUMS="$OUT/SHA256SUMS.txt"
BASELINE_HEAD="2d4dbb83559cab0ac85753b6cec5c8fd3139522c"

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

rm -f "$EXE"
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
schema=AERIS39_MAPSO2A_DEPENDENCY_PROVENANCE_V1
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

raw = pathlib.Path(txt_name).read_text(encoding="utf-8", errors="replace")
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
for block in raw.split("--- METHOD ---")[1:]:
    item = {"instructions": []}
    for original in block.splitlines():
        line = original.strip()
        if not line:
            continue
        if line.startswith("IL "):
            item["instructions"].append(line)
            continue
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if key in {
            "signature", "declaring_type", "metadata_token", "attributes", "impl_flags",
            "module_name", "module_mvid", "module_path", "module_sha256",
            "managed_body", "body_error", "il_bytes", "il_hex", "il_sha256",
            "max_stack", "init_locals", "locals"
        }:
            if key in {"managed_body", "init_locals"}:
                item[key] = as_bool(value)
            elif key in {"il_bytes", "max_stack", "locals"}:
                item[key] = as_int(value)
            else:
                item[key] = value
    if "signature" in item:
        methods.append(item)

summary_keys = [
    "method_count",
    "managed_body_count",
    "nonmanaged_body_count",
    "mapso_cctor_present",
    "required_MapSO_GetPixelFloat",
    "required_MapSO_GetPixelColor32",
    "required_MapSO_GetPixelColor",
    "dependency_Mathf_Lerp",
    "dependency_Color_Lerp",
    "dependency_Color32_Lerp",
    "dependency_Color32_op_Implicit",
    "AERIS39_MAPSO2A_DEPENDENCY_DUMP",
]
summary = {key: first_value(key) for key in summary_keys}

payload = {
    "schema": "AERIS39_MAPSO2A_DEPENDENCY_DUMP_V1",
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
    "dump_exit_code": as_int(dump_rc),
    "text_sha256": hashlib.sha256(raw.encode("utf-8")).hexdigest(),
    "summary": summary,
    "methods": methods,
}

pathlib.Path(json_name).write_text(
    json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

(
    cd "$OUT"
    sha256sum mapso2a_dependency_dump.txt mapso2a_dependency_dump.json provenance.txt > SHA256SUMS.txt
)

echo
echo "=== AERIS39 MAPSO-2A ARTIFACTS ==="
echo "output=$OUT"
echo "dump_exit_code=$DUMP_RC"
grep -E '^(method_count|managed_body_count|nonmanaged_body_count|mapso_cctor_present|required_MapSO_|dependency_|AERIS39_MAPSO2A_DEPENDENCY_DUMP)=' "$TXT" || true
echo
echo "=== SHA256 ==="
cat "$SUMS"

if [[ $DUMP_RC -ne 0 ]]; then
    echo "AERIS39_MAPSO2A_CAPTURE=FAIL" >&2
    exit "$DUMP_RC"
fi

echo "AERIS39_MAPSO2A_CAPTURE=PASS"
