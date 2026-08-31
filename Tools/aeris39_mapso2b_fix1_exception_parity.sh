#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <KSP root> [output directory]" >&2
    exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
OUT="${2:-$HOME/Desktop/AERIS39_MAPSO2B_FIX1_Exception_Parity}"
MANAGED="$KSP/KSP_x64_Data/Managed"
ASSEMBLY="$MANAGED/Assembly-CSharp.dll"
CORE="$MANAGED/UnityEngine.CoreModule.dll"
BASE_SRC="$ROOT/Tools/AERIS39_MAPSO2B_native_purecpu_witness.cs"
PATCHER="$ROOT/Tools/AERIS39_MAPSO2B_fix1_exception_parity_patch.py"
PATCHED_SRC="/tmp/AERIS39_MAPSO2B_fix1_exception_parity.cs"
EXE="/tmp/AERIS39_MAPSO2B_fix1_exception_parity.exe"
TXT="$OUT/mapso2b_fix1_exception_parity.txt"
JSON="$OUT/mapso2b_fix1_exception_parity.json"
PROVENANCE="$OUT/provenance.txt"
SUMS="$OUT/SHA256SUMS.txt"

for FILE in "$ASSEMBLY" "$CORE" "$BASE_SRC" "$PATCHER"; do
    [[ -f "$FILE" ]] || { echo "FAIL: missing $FILE" >&2; exit 3; }
done
for CMD in mono python3 sha256sum git; do
    command -v "$CMD" >/dev/null 2>&1 || { echo "FAIL: missing command $CMD" >&2; exit 4; }
done

COMPILER=""
if command -v mcs >/dev/null 2>&1; then COMPILER="mcs";
elif command -v csc >/dev/null 2>&1; then COMPILER="csc";
else echo "FAIL: neither mcs nor csc found" >&2; exit 4; fi

mkdir -p "$OUT"
rm -f "$TXT" "$JSON" "$PROVENANCE" "$SUMS" "$PATCHED_SRC" "$EXE"

BRANCH="$(git -C "$ROOT" branch --show-current 2>/dev/null || true)"
HEAD="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || true)"
UTC="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
BASE_SHA="$(sha256sum "$BASE_SRC" | awk '{print $1}')"
PATCHER_SHA="$(sha256sum "$PATCHER" | awk '{print $1}')"
WRAPPER_SHA="$(sha256sum "${BASH_SOURCE[0]}" | awk '{print $1}')"

python3 "$PATCHER" "$BASE_SRC" "$PATCHED_SRC"
PATCHED_SHA="$(sha256sum "$PATCHED_SRC" | awk '{print $1}')"

if [[ "$COMPILER" == "mcs" ]]; then
    mcs -nologo -optimize+ -out:"$EXE" "$PATCHED_SRC"
    COMPILER_VERSION="$(mcs --version 2>/dev/null | head -n 1 || true)"
else
    csc -nologo -optimize+ -out:"$EXE" "$PATCHED_SRC"
    COMPILER_VERSION="$(csc -version 2>/dev/null | head -n 1 || true)"
fi
MONO_VERSION="$(mono --version 2>/dev/null | head -n 1 || true)"

set +e
mono "$EXE" "$MANAGED" 2>&1 | tee "$TXT"
RC=${PIPESTATUS[0]}
set -e

cat > "$PROVENANCE" <<EOF
schema=AERIS39_MAPSO2B_FIX1_EXCEPTION_PARITY_V1
captured_utc=$UTC
helper_branch=$BRANCH
helper_git_head=$HEAD
base_witness=$BASE_SRC
base_witness_sha256=$BASE_SHA
patcher=$PATCHER
patcher_sha256=$PATCHER_SHA
patched_source_sha256=$PATCHED_SHA
wrapper=${BASH_SOURCE[0]}
wrapper_sha256=$WRAPPER_SHA
assembly_sha256=$ASSEMBLY_SHA
unity_core_sha256=$CORE_SHA
compiler=$COMPILER
compiler_version=$COMPILER_VERSION
mono_version=$MONO_VERSION
witness_exit_code=$RC
production_authority=PQS
db_authority=PQS
producer_switch=false
db_write=false
preload_mutation=false
EOF

python3 - "$TXT" "$JSON" "$PROVENANCE" "$RC" <<'PY'
import hashlib, json, pathlib, sys
raw = pathlib.Path(sys.argv[1]).read_text(encoding='utf-8', errors='replace')
lines = raw.splitlines()
def first(k):
    p=k+'='
    for line in lines:
        if line.startswith(p): return line[len(p):]
    return None
def i(v):
    try:return int(v)
    except:return None
def b(v):
    if v is None:return None
    return v.lower()=='true'
keys=['managed_identity_exact','constant_bits_exact','snapshots','coordinate_pairs','float_checks','color_checks','color32_checks','total_checks','mismatch_count','exception_match_count','exception_mismatch_count','max_abs_error','bit_exact','AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS']
s={k:first(k) for k in keys}
for k in ['snapshots','coordinate_pairs','float_checks','color_checks','color32_checks','total_checks','mismatch_count','exception_match_count','exception_mismatch_count']: s[k]=i(s[k])
for k in ['managed_identity_exact','constant_bits_exact','bit_exact']: s[k]=b(s[k])
p={
 'schema':'AERIS39_MAPSO2B_FIX1_EXCEPTION_PARITY_V1',
 'witness_exit_code':i(sys.argv[4]),
 'text_sha256':hashlib.sha256(raw.encode()).hexdigest(),
 'summary':s,
 'mismatches':[x for x in lines if x.startswith('MISMATCH ')],
 'provenance_sha256':hashlib.sha256(pathlib.Path(sys.argv[3]).read_bytes()).hexdigest(),
}
pathlib.Path(sys.argv[2]).write_text(json.dumps(p,indent=2,sort_keys=True)+'\n',encoding='utf-8')
PY

(
  cd "$OUT"
  sha256sum mapso2b_fix1_exception_parity.txt mapso2b_fix1_exception_parity.json provenance.txt > SHA256SUMS.txt
)

echo
echo "=== AERIS39 MAPSO-2B FIX1 ARTIFACTS ==="
echo "output=$OUT"
echo "witness_exit_code=$RC"
grep -E '^(managed_identity_exact|constant_bits_exact|snapshots|coordinate_pairs|float_checks|color_checks|color32_checks|total_checks|mismatch_count|exception_match_count|exception_mismatch_count|max_abs_error|bit_exact|AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS)=' "$TXT" || true
echo
echo "=== SHA256 ==="
cat "$SUMS"

if [[ $RC -ne 0 ]]; then
    echo "AERIS39_MAPSO2B_FIX1_CAPTURE=FAIL" >&2
    exit "$RC"
fi

echo "AERIS39_MAPSO2B_FIX1_CAPTURE=PASS"
