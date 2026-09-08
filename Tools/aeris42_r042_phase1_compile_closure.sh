#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase1_compile_closure.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R042CompileClosure.csproj"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"

cleanup() {
  rm -f "$TEMP_PROJECT"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE1 COMPILE CLOSURE ==="
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

bash "$ROOT/Tools/aeris42_r042_start_audit.sh"

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
from pathlib import Path
import sys

source = Path(sys.argv[1])
target = Path(sys.argv[2])
text = source.read_text(encoding="utf-8")

files = [
    r"Terrain\AERIS39MapSoPureCpuExact.cs",
    r"Terrain\AERIS39HeightMapPureCpuExact.cs",
    r"Terrain\AERIS39MapDecalPureCpuExact.cs",
    r"Terrain\AERIS39MapDecalTangentPureCpuExact.cs",
    r"Terrain\AERIS39FlattenAreaPureCpuExact.cs",
    r"Terrain\AERIS39LandControlPureCpuExact.cs",
    r"Terrain\AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs",
    r"Terrain\AERIS41VertexVoronoiPureCpuExact.cs",
    r"Terrain\AERIS39AllBodyHeightModifierChainPureCpuExact.cs",
]

missing = [p for p in files if f'<Compile Include="{p}" />' not in text]
marker = '    <Compile Include="Terrain\\AERISTerrainPreloadCodec.cs" />'
if missing:
    if marker not in text:
        raise SystemExit("STOP: csproj insertion marker not found")
    block = "".join(f'    <Compile Include="{p}" />\n' for p in missing)
    text = text.replace(marker, block + marker, 1)

target.write_text(text, encoding="utf-8")
print(f"temporary_compile_entries_added={len(missing)}")
for path in missing:
    print(f"temporary_compile_added={path}")
PY

for name in \
  AERIS39MapSoPureCpuExact.cs \
  AERIS39HeightMapPureCpuExact.cs \
  AERIS39MapDecalPureCpuExact.cs \
  AERIS39MapDecalTangentPureCpuExact.cs \
  AERIS39FlattenAreaPureCpuExact.cs \
  AERIS39LandControlPureCpuExact.cs \
  AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs \
  AERIS41VertexVoronoiPureCpuExact.cs \
  AERIS39AllBodyHeightModifierChainPureCpuExact.cs; do
  grep -Fq "Terrain\\$name" "$TEMP_PROJECT" || {
    echo "STOP: temporary compile closure missing $name" >&2
    exit 20
  }
done

echo
echo "=== XBUILD / PURE EXACT PERMANENT-COMPILE CANDIDATE ==="
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
echo "=== R042 PHASE1 RESULT ==="
echo "dll=$DLL"
echo "dll_sha256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "runtime_behavior_changed=false"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_write_switch=false"
echo "R042_PHASE1_COMPILE_CLOSURE=PASS"
echo "next=PERMANENT_CSPROJ_PROMOTION"
