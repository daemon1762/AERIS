#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase2_permanent_compile.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
LOG="${TMPDIR:-/tmp}/AERIS42_R042_phase2_build.$$.log"

cleanup() {
  rm -f "$LOG"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE2 PERMANENT COMPILE ==="
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
command -v xbuild >/dev/null 2>&1 || {
  echo "STOP: xbuild not found" >&2
  exit 12
}

sources=(
  AERIS39MapSoPureCpuExact.cs
  AERIS39HeightMapPureCpuExact.cs
  AERIS39MapDecalPureCpuExact.cs
  AERIS39MapDecalTangentPureCpuExact.cs
  AERIS39FlattenAreaPureCpuExact.cs
  AERIS39LandControlPureCpuExact.cs
  AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs
  AERIS41VertexVoronoiPureCpuExact.cs
  AERIS39AllBodyHeightModifierChainPureCpuExact.cs
)

for name in "${sources[@]}"; do
  path="Source/AERISFlightControl/Terrain/$name"
  [[ -f "$path" ]] || {
    echo "STOP: source missing $name" >&2
    exit 20
  }
  grep -Fq "Terrain\\$name" "$PROJECT" || {
    echo "STOP: permanent csproj entry missing $name" >&2
    exit 21
  }
  echo "permanent_compile_present=$name"
done

echo
echo "=== XBUILD / PERMANENT PROJECT ==="
(
  cd "$PROJECT_DIR"
  xbuild \
    /p:Configuration=Release \
    /p:KSPDIR="$KSP" \
    "$(basename "$PROJECT")"
) 2>&1 | tee "$LOG"

for name in "${sources[@]}"; do
  grep -Fq "Terrain/$name" "$LOG" || {
    echo "STOP: compiler command did not include $name" >&2
    exit 30
  }
  echo "compiler_input_confirmed=$name"
done

[[ -f "$DLL" ]] || {
  echo "STOP: expected DLL not produced" >&2
  exit 31
}

echo
echo "=== R042 PHASE2 RESULT ==="
echo "dll=$DLL"
echo "dll_sha256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "permanent_compile_count=9"
echo "runtime_behavior_changed=false"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_write_switch=false"
echo "R042_PHASE2_PERMANENT_COMPILE=PASS"
echo "next=R042_SNAPSHOT_AND_SOURCE_RESOLVER_DESIGN"
