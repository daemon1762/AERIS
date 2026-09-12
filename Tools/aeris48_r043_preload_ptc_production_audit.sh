#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris48_r043_preload_ptc_production_audit.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris48-r043-preload-ptc-production-audit"
ACCEPTED_SHA="8b9f48a8592dc6e4e2e241b69a65ffde21359991"

CSProj="Source/AERISFlightControl/AERISFlightControl.csproj"
PIPE="Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs"
SHADOW="Source/AERISFlightControl/Terrain/AERISR043PreloadPtcIntegratedShadow.cs"
LIVE="Source/AERISFlightControl/Terrain/AERISR043PreloadPtcLiveProof.cs"
PROD="Source/AERISFlightControl/Terrain/AERISR047ExactCpuProduction.cs"
RESOLVER="Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
TILES="Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs"
DBSRC="Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs"
DB="$KSP/GameData/AERISFlightControl/PluginData/TerrainPreloadDatabaseV3"

cd "$ROOT"

echo "=== AERIS48 / R043 PRELOAD PTC PRODUCTION AUDIT ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "accepted_base=$ACCEPTED_SHA"
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
  echo "STOP: AERIS47 accepted base is not an ancestor" >&2
  exit 12
}

fail=0
checks=0
pass(){
  checks=$((checks+1))
  echo "PASS $1"
}
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
require_absent(){
  local pattern="$1" f="$2" label="$3"
  if ! grep -Fq "$pattern" "$f"; then pass "$label"; else
    echo "FAIL $label unexpected_pattern=$pattern file=$f"
    fail=$((fail+1))
  fi
}

for pair in   "$CSProj|csproj"   "$PIPE|block_pipeline"   "$SHADOW|ptc_integrated_seam"   "$LIVE|ptc_live_proof_seam"   "$PROD|exact_cpu_production"   "$RESOLVER|source_resolver"   "$TILES|tile_system"   "$DBSRC|preload_database"
do
  require_file "${pair%%|*}" "${pair##*|}"
done

require_grep 'AERISR043PreloadPtcIntegratedShadow.cs' "$CSProj"   "integrated_ptc_seam_compiled"
require_grep 'AERISR047ExactCpuProduction.cs' "$CSProj"   "exact_cpu_production_compiled"
require_grep 'AERISR043PreloadPtcLiveProof.cs' "$CSProj"   "legacy_live_proof_seam_compiled"
require_grep 'ProducerSwitchEnabled = true' "$RESOLVER"   "exact_cpu_producer_switch_enabled"
require_grep 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID' "$TILES"   "env4_hybrid_environment_contract"
require_grep 'request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder' "$SHADOW"   "ptc_scoped_to_preload_builder"
require_grep 'R047ShouldUseExactCpuProduction' "$SHADOW"   "ptc_source_selection_routes_to_r047"
require_grep 'R047TryCaptureExactCpuProductionSample' "$PIPE"   "block_pipeline_exact_cpu_capture"
require_grep 'No PQS terrain-height callback is issued.' "$PIPE"   "exact_cpu_path_skips_pqs_height_callback"
require_grep 'ProductionElevation = state.SamplingElevation' "$SHADOW"   "exact_cpu_worker_targets_production_tile_buffer"
require_grep 'R047WriteExactCpuProductionSample' "$SHADOW"   "worker_writes_exact_cpu_production_samples"
require_grep 'tile_commit_authority=' "$SHADOW"   "tile_commit_authority_telemetry"
require_grep 'db_authority=' "$SHADOW"   "db_authority_telemetry"
require_grep 'exact_cpu_db_write=' "$SHADOW"   "exact_cpu_db_write_telemetry"
require_grep 'TerrainPreloadDatabaseV3' "$TILES"   "preload_database_v3_active"
require_grep 'manifest.atm' "$DBSRC"   "preload_manifest_persistence"
require_grep 'Chunks' "$DBSRC"   "preload_chunk_persistence"

# These old observer source files may remain in the tree as historical tooling,
# but must not be part of the canonical production build.
require_absent 'AERISR043PreloadPtcIntegratedPipelineObserver.cs' "$CSProj"   "old_integrated_observer_not_canonical"
require_absent 'AERISR043PreloadPtcLivePreloadObserver.cs' "$CSProj"   "old_live_preload_observer_not_canonical"
require_absent 'AERISR043PreloadPtcShadowTileGridObserver.cs' "$CSProj"   "old_tilegrid_observer_not_canonical"

echo
echo "=== AERIS48 PTC PRODUCTION INVENTORY ==="
echo "production_chain=RuntimeBodySnapshot -> R042SourceResolver -> R047ExactCpu -> BlockPipelineWorker -> SamplingElevation -> PreloadBuilder -> TerrainPreloadDatabaseV3"
echo "exact_cpu_bodies=Kerbin,Eve,Duna,Dres,Moho,Eeloo,Minmus"
echo "pqs_fallback_bodies=Mun,Ike,Laythe,Vall,Bop,Tylo,Gilly,Pol"
echo "environment_contract=ENV4_EXACTCPU_HYBRID"
echo "legacy_shadow_scaffold=true"
echo "legacy_live_proof_scaffold=true"
echo "historical_r043_observers_canonical=false"

if [[ -d "$DB" ]]; then
  echo "active_db_present=true"
  echo "active_db_bytes=$(du -sb "$DB" | awk '{print $1}')"
  echo "active_db_files=$(find "$DB" -type f | wc -l | tr -d ' ')"
else
  echo "active_db_present=false"
fi

echo
echo "checks_passed=$checks"
echo "checks_failed=$fail"

if (( fail != 0 )); then
  echo "AERIS48_PTC_AUDIT_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=PRELOAD_PTC_PRODUCTION_AUDIT_FAIL"
  exit 40
fi

echo "AERIS48_PTC_AUDIT_VERDICT=READY_FOR_PRODUCTION_CLEANUP"
echo "AERIS_CURRENT_STAGE=PRELOAD_PTC_PRODUCTION_AUDIT_PASS"
echo "next_action=Separate legacy proof/shadow scaffolding from the already-active production chain without changing generated terrain or DB bytes."
