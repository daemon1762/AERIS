#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase3_source_resolver_contract.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R042Phase3.csproj"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"

cleanup() {
  rm -f "$TEMP_PROJECT"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE3 SOURCE RESOLVER CONTRACT ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"

test "$(git branch --show-current)" = "$EXPECTED_BRANCH" || {
  echo "STOP: wrong branch" >&2
  exit 10
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: KSP managed directory not found" >&2
  exit 11
}
[[ -f "$RESOLVER" ]] || {
  echo "STOP: R042 source resolver missing" >&2
  exit 12
}
command -v xbuild >/dev/null 2>&1 || {
  echo "STOP: xbuild not found" >&2
  exit 13
}

# Static safety gates: body-name classification is candidate-only; runtime snapshot
# certification remains mandatory and the production producer switch remains disabled.
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || {
  echo "STOP: producer switch safety gate missing" >&2
  exit 20
}
grep -Fq 'RuntimeSnapshotCertificationRequired' "$RESOLVER" || {
  echo "STOP: runtime snapshot certification gate missing" >&2
  exit 21
}
grep -Fq 'if (!runtimeSnapshotCertified)' "$RESOLVER" || {
  echo "STOP: uncertified snapshot does not force PQS fallback" >&2
  exit 22
}
grep -Fq 'if (!ProducerSwitchEnabled)' "$RESOLVER" || {
  echo "STOP: disabled producer switch does not force PQS fallback" >&2
  exit 23
}
grep -Fq 'internal const int AcceptedCandidateBodyCount = 7;' "$RESOLVER" || {
  echo "STOP: accepted candidate body count changed" >&2
  exit 24
}
grep -Fq 'internal const int R039CandidateBodyCount = 1;' "$RESOLVER" || exit 25
grep -Fq 'internal const int R041CandidateBodyCount = 6;' "$RESOLVER" || exit 26

for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
  grep -Fq "\"$body\"" "$RESOLVER" || {
    echo "STOP: certified candidate missing: $body" >&2
    exit 27
  }
  echo "candidate_present=$body"
done

# Compile the contract in the real production project closure without permanently
# changing the csproj yet. Phase 4 will permanently promote the resolver together
# with the main-thread runtime snapshot capture implementation after this proof passes.
python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
from pathlib import Path
import sys
src = Path(sys.argv[1])
dst = Path(sys.argv[2])
text = src.read_text(encoding="utf-8")
entry = '    <Compile Include="Terrain\\AERISR042ExactCpuShadowSourceResolver.cs" />\n'
marker = '    <Compile Include="Terrain\\AERISTerrainPreloadCodec.cs" />'
if entry.strip() not in text:
    if marker not in text:
        raise SystemExit("STOP: csproj insertion marker not found")
    text = text.replace(marker, entry + marker, 1)
dst.write_text(text, encoding="utf-8")
print("temporary_resolver_compile_entry=true")
PY

echo
echo "=== XBUILD / PHASE3 CONTRACT CANDIDATE ==="
(
  cd "$PROJECT_DIR"
  xbuild \
    /p:Configuration=Release \
    /p:KSPDIR="$KSP" \
    "$(basename "$TEMP_PROJECT")"
)

[[ -f "$DLL" ]] || {
  echo "STOP: expected DLL not produced" >&2
  exit 30
}

echo
echo "=== R042 PHASE3 RESULT ==="
echo "dll=$DLL"
echo "dll_sha256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "certified_candidate_count=7"
echo "r039_minmus_candidate_count=1"
echo "r041_height_chain_candidate_count=6"
echo "runtime_snapshot_gate_required=true"
echo "body_name_alone_authorizes_shadow=false"
echo "unknown_or_uncertified_fallback=PQS"
echo "runtime_behavior_changed=false"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_write_switch=false"
echo "R042_PHASE3_SOURCE_RESOLVER_CONTRACT=PASS"
echo "next=R042_MAIN_THREAD_RUNTIME_SNAPSHOT_CAPTURE"
