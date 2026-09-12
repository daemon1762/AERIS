#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris49_terrain_nd_integration_audit.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris49-terrain-nd-integration-audit"
ACCEPTED_SHA="7f300b125fa7dc06e383dc1464da3af5d0396065"

ND="Source/AERISFlightControl/UI/AERISNavigationDisplay.cs"
GPU="Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
PROJ="Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"
RESIDENT="Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs"
TILES="Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs"
PTC="Source/AERISFlightControl/Terrain/AERISR043PreloadPtcPipeline.cs"
DB="$KSP/GameData/AERISFlightControl/PluginData/TerrainPreloadDatabaseV3"

# Accepted R023 ND-core blob identities. These three files remain byte-identical in
# AERIS49; the renderer and TileSystem intentionally evolved after R023.
R023_ND_SHA="2a73bbc9e78c9f4d5b1facd3cda61b07ba64750c"
R023_PROJ_SHA="b15c901ba6a167e3935793c0bfb69edb8a2838c8"
R023_RESIDENT_SHA="d514a1fe6e77dffde004e388440f790ad21cce8e"

cd "$ROOT"

echo "=== AERIS49 / TERRAIN_ND INTEGRATION AUDIT ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "accepted_preload_ptc=$ACCEPTED_SHA"
echo "KSP=$KSP"

[[ "$(git branch --show-current)" = "$EXPECTED_BRANCH" ]] || {
  echo "STOP: wrong branch" >&2
  exit 10
}
[[ -z "$(git status --porcelain)" ]] || {
  echo "STOP: worktree dirty" >&2
  git status -sb >&2
  exit 11
}
git merge-base --is-ancestor "$ACCEPTED_SHA" HEAD || {
  echo "STOP: accepted PRELOAD_PTC base is not an ancestor" >&2
  exit 12
}

fail=0
checks=0
pass(){ checks=$((checks+1)); echo "PASS $1"; }
require_file(){
  local f="$1" label="$2"
  if [[ -f "$f" ]]; then pass "$label"; else
    echo "FAIL $label missing=$f"
    fail=$((fail+1))
  fi
}
require_grep(){
  local pattern="$1" f="$2" label="$3"
  if grep -Fq "$pattern" "$f"; then pass "$label"; else
    echo "FAIL $label pattern=$pattern file=$f"
    fail=$((fail+1))
  fi
}
require_blob(){
  local f="$1" expected="$2" label="$3"
  local actual
  actual="$(git hash-object "$f")"
  if [[ "$actual" = "$expected" ]]; then
    pass "$label"
  else
    echo "FAIL $label expected=$expected actual=$actual file=$f"
    fail=$((fail+1))
  fi
}

for pair in   "$ND|navigation_display"   "$GPU|terrain_gpu_renderer"   "$PROJ|nd_gpu_projection_backend"   "$RESIDENT|resident_cache"   "$TILES|terrain_tile_system"   "$PTC|accepted_ptc_pipeline"
do
  require_file "${pair%%|*}" "${pair##*|}"
done

# Accepted ND core survived the PTC/ENV4 work byte-for-byte.
require_blob "$ND" "$R023_ND_SHA" "r023_navigation_display_identity"
require_blob "$PROJ" "$R023_PROJ_SHA" "r023_gpu_projection_identity"
require_blob "$RESIDENT" "$R023_RESIDENT_SHA" "r023_resident_cache_identity"

# ND -> renderer -> TileSystem -> Resident/ENV4 chain.
require_grep 'AERISTerrainTileSystem tileSystem = terrain == null ? null :' "$ND"   "nd_reads_canonical_tile_system"
require_grep 'terrain.DisplayTiles;' "$ND"   "nd_display_tiles_authority"
require_grep 'terrainTileRenderer.Draw(plot, tileSystem, vessel,' "$ND"   "nd_routes_terrain_to_gpu_renderer"
require_grep 'AERISTerrainGpuDrawState.Complete' "$ND"   "nd_complete_gpu_present_gate"
require_grep 'AERISTerrainGpuDrawState.Partial' "$ND"   "nd_partial_gpu_present_gate"

require_grep 'internal AERISTerrainGpuDrawState Draw(Rect plot,' "$GPU"   "renderer_draw_entrypoint"
require_grep 'AERISTerrainTileSystem system, Vessel vessel' "$GPU"   "renderer_uses_tile_system"
require_grep 'residentCache = system.CurrentBodyResidentCache;' "$GPU"   "renderer_uses_current_body_resident_cache"
require_grep 'readonly AERISNdGpuVertexProjectionBackend gpuVertexProjection =' "$GPU"   "renderer_uses_nd_gpu_projection_backend"
require_grep 'Rev35R023Variant = "AERIS30_REV3_5_SALBUTAMOL_SULFATE_R023_BOUNDED_PREWARM_ADMISSION_PACING"' "$GPU"   "r023_renderer_admission_survives"
require_grep 'Rev35R024Variant = "AERIS30_REV3_5_SALBUTAMOL_SULFATE_R024_EXACT_VISIBLE_COMMIT_PREEMPTION"' "$GPU"   "post_r023_visible_commit_path_survives"

require_grep 'internal AERISTerrainVisibleTileSet CaptureVisible(' "$TILES"   "tile_system_visible_capture"
require_grep 'currentBodyResidentCache.BeginBody(' "$TILES"   "tile_system_resident_scope_sync"
require_grep 'EnvironmentHashForBody(body)' "$TILES"   "tile_system_resident_env4_identity"
require_grep 'TerrainPreloadDatabaseV3' "$TILES"   "tile_system_uses_preload_database_v3"
require_grep 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID|' "$TILES"   "tile_system_uses_env4_hybrid_contract"
require_grep 'Rev35R020Variant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY"' "$TILES"   "visible_authority_stability_survives"

# PTC/ND boundary: ND does not choose producer authority. It consumes committed tiles.
require_grep 'R047ShouldUseExactCpuProduction' "$PTC"   "ptc_keeps_producer_selection"
require_grep 'ProductionElevation = state.SamplingElevation' "$PTC"   "ptc_writes_committed_tile_payload"

echo
echo "=== TERRAIN_ND INVENTORY ==="
echo "nd_core_baseline=AERIS30_R023_BYTE_IDENTICAL"
echo "navigation_display_sha=$(git hash-object "$ND")"
echo "projection_backend_sha=$(git hash-object "$PROJ")"
echo "resident_cache_sha=$(git hash-object "$RESIDENT")"
echo "renderer_policy=R023_PLUS_POST_R023_EVOLUTION"
echo "tile_authority=TerrainTileSystem"
echo "resident_authority=CurrentBodyResidentCache"
echo "database=TerrainPreloadDatabaseV3"
echo "environment_contract=ENV4_EXACTCPU_HYBRID"
echo "producer_selection=PRELOAD_PTC"
echo "nd_producer_awareness=false"
echo "nd_consumes_committed_tiles=true"

if [[ -d "$DB" ]]; then
  echo "active_db_present=true"
  echo "active_db_bytes=$(du -sb "$DB" | awk '{print $1}')"
  echo "active_db_files=$(find "$DB" -type f -printf '.' | wc -c | tr -d ' ')"
else
  echo "active_db_present=false"
fi

echo
echo "checks_passed=$checks"
echo "checks_failed=$fail"

if (( fail != 0 )); then
  echo "AERIS49_TERRAIN_ND_AUDIT_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=TERRAIN_ND_INTEGRATION_AUDIT_FAIL"
  exit 40
fi

echo "AERIS49_TERRAIN_ND_AUDIT_VERDICT=READY_FOR_RUNTIME_GATE"
echo "AERIS_CURRENT_STAGE=TERRAIN_ND_INTEGRATION_AUDIT_PASS"
echo "next_action=Build a runtime proof that ENV4 committed terrain reaches the existing R023 ND presentation chain without producer leakage or DB mutation."
