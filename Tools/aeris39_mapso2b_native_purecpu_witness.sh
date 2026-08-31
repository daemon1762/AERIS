#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <KSP root> [output directory]" >&2
    exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
OUT="${2:-$HOME/Desktop/AERIS39_MAPSO2B_Native_PureCPU_Witness}"
MANAGED="$KSP/KSP_x64_Data/Managed"
ASSEMBLY="$MANAGED/Assembly-CSharp.dll"
CORE="$MANAGED/UnityEngine.CoreModule.dll"
SRC="$ROOT/Tools/AERIS39_MAPSO2B_native_purecpu_witness.cs"
EXE="/tmp/AERIS39_MAPSO2B_native_purecpu_witness.exe"
TXT="$OUT/mapso2b_native_purecpu_witness.txt"
JSON="$OUT/mapso2b_native_purecpu_witness.json"
PROVENANCE="$OUT/provenance.txt"
SUMS="$OUT/SHA256SUMS.txt"
MAPSO2A_BASELINE="2d71a58e58933a32814114cf965f44a192432590"
MAPSO2A_TXT_SHA="bf21b787cff1731375a0845d1afc3508f4283189f1ead68f81a4cd851b8f66a5"
MAPSO2A_JSON_SHA="1aa5174a43420c33c156f4a71235bfa1a77dba49aff3665335baf7d4a5231302"
MAPSO2A_PROVENANCE_SHA="236c1c0f423efc42605603c7ab1b6d85bdeb47832437e2f43477794ec575dca6"

for FILE in "$ASSEMBLY" "$CORE" "$SRC"; do
    [[ -f "$FILE" ]] || {
        echo "FAIL: required file missing: $FILE" >&2
        exit 3
    }
done

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
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
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
WITNESS_RC=${PIPESTATUS[0]}
set -e

cat > "$PROVENANCE" <<EOF
schema=AERIS39_MAPSO2B_NATIVE_PURECPU_PROVENANCE_V1
captured_utc=$UTC
mapso2a_baseline_head=$MAPSO2A_BASELINE
mapso2a_txt_sha256=$MAPSO2A_TXT_SHA
mapso2a_json_sha256=$MAPSO2A_JSON_SHA
mapso2a_provenance_sha256=$MAPSO2A_PROVENANCE_SHA
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
unity_core=$CORE
unity_core_sha256=$CORE_SHA
compiler=$COMPILER
compiler_version=$COMPILER_VERSION
mono_version=$MONO_VERSION
witness_exit_code=$WITNESS_RC
production_authority=PQS
db_authority=PQS
producer_switch=false
db_write=false
preload_mutation=false
EOF

python3 - "$TXT" "$JSON" "$PROVENANCE" "$BRANCH" "$HEAD" "$SOURCE_SHA" "$WRAPPER_SHA" "$WITNESS_RC" "$UTC" <<'PY'
import hashlib
import json
import pathlib
import sys

(
    txt_name,
    json_name,
    provenance_name,
    helper_branch,
    helper_head,
    source_sha,
    wrapper_sha,
    witness_rc,
    captured_utc,
) = sys.argv[1:]

raw = pathlib.Path(txt_name).read_text(encoding="utf-8", errors="replace")
lines = raw.splitlines()

def first(key):
    prefix = key + "="
    for line in lines:
        if line.startswith(prefix):
            return line[len(prefix):]
    return None

def as_int(v):
    try:
        return int(v)
    except (TypeError, ValueError):
        return None

def as_bool(v):
    if v is None:
        return None
    return v.strip().lower() == "true"

summary_keys = [
    "assembly_sha256",
    "mapso_mvid",
    "unity_core_sha256",
    "unity_core_mvid",
    "managed_identity_exact",
    "native_Byte2Float",
    "native_Byte2Float_bits",
    "native_Float2Byte",
    "native_Float2Byte_bits",
    "constant_bits_exact",
    "snapshots",
    "coordinate_pairs",
    "float_checks",
    "color_checks",
    "color32_checks",
    "total_checks",
    "mismatch_count",
    "max_abs_error",
    "bit_exact",
    "production_authority",
    "db_authority",
    "producer_switch",
    "db_write",
    "preload_mutation",
    "diagnostic_runtime_object_invocation",
    "production_worker_runtime_object_access",
    "AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS",
]

summary = {k: first(k) for k in summary_keys}
for k in ["snapshots", "coordinate_pairs", "float_checks", "color_checks", "color32_checks", "total_checks", "mismatch_count"]:
    summary[k] = as_int(summary.get(k))
for k in ["managed_identity_exact", "constant_bits_exact", "bit_exact", "producer_switch", "db_write", "preload_mutation", "diagnostic_runtime_object_invocation", "production_worker_runtime_object_access"]:
    summary[k] = as_bool(summary.get(k))

mismatches = [line for line in lines if line.startswith("MISMATCH ")]
provenance_raw = pathlib.Path(provenance_name).read_text(encoding="utf-8", errors="replace")

payload = {
    "schema": "AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS_V1",
    "captured_utc": captured_utc,
    "helper_branch": helper_branch,
    "helper_git_head": helper_head,
    "helper_source_sha256": source_sha,
    "wrapper_sha256": wrapper_sha,
    "witness_exit_code": as_int(witness_rc),
    "text_sha256": hashlib.sha256(raw.encode("utf-8")).hexdigest(),
    "provenance_sha256": hashlib.sha256(provenance_raw.encode("utf-8")).hexdigest(),
    "summary": summary,
    "mismatches": mismatches,
}

pathlib.Path(json_name).write_text(
    json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

(
    cd "$OUT"
    sha256sum mapso2b_native_purecpu_witness.txt mapso2b_native_purecpu_witness.json provenance.txt > SHA256SUMS.txt
)

echo
echo "=== AERIS39 MAPSO-2B ARTIFACTS ==="
echo "output=$OUT"
echo "witness_exit_code=$WITNESS_RC"
grep -E '^(managed_identity_exact|constant_bits_exact|snapshots|coordinate_pairs|float_checks|color_checks|color32_checks|total_checks|mismatch_count|max_abs_error|bit_exact|production_authority|db_authority|producer_switch|db_write|preload_mutation|AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS)=' "$TXT" || true

echo
echo "=== SHA256 ==="
cat "$SUMS"

if [[ $WITNESS_RC -ne 0 ]]; then
    echo "AERIS39_MAPSO2B_CAPTURE=FAIL" >&2
    exit "$WITNESS_RC"
fi

echo "AERIS39_MAPSO2B_CAPTURE=PASS"
