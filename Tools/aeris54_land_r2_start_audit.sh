#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris54_land_r2_start_audit.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris54-land-r2-immutable-terrain-corridor-snapshots"
BASE="86e7fa0135dfc07016672383e0a93fc3f4dce982"

cd "$ROOT"

echo "=== AERIS54 / LAND-R2 START AUDIT ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "base_producer_coherence_accepted=$BASE"
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

git merge-base --is-ancestor "$BASE" HEAD || {
  echo "STOP: AERIS53 accepted base is not an ancestor" >&2
  exit 12
}

[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: invalid KSP root" >&2
  exit 13
}

TILE="Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs"
DB="Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs"
MODELS="Source/AERISFlightControl/Landing/AERISApproachModels.cs"
PLANNER="Source/AERISFlightControl/Landing/AERISAdaptiveApproachPlanner.cs"
REGISTRY="Source/AERISFlightControl/Landing/AERISApproachRegistry.cs"
FOUNDATION="Source/AERISFlightControl/Landing/AERISLandingFoundation.cs"

grep -Fq 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID_BODY_FP_HF2|' "$TILE" || {
  echo "STOP: accepted AERIS53 HF2 terrain identity missing" >&2
  exit 20
}

grep -Fq 'internal sealed class AERISApproachObstacleSnapshot' "$MODELS" || {
  echo "STOP: approach obstacle snapshot model missing" >&2
  exit 21
}

grep -Fq 'internal bool CorridorComplete;' "$MODELS" || {
  echo "STOP: corridor completeness gate missing" >&2
  exit 22
}

grep -Fq 'if (obstacles == null || !obstacles.CorridorComplete)' "$PLANNER" || {
  echo "STOP: planner no longer fails closed on incomplete corridor" >&2
  exit 23
}

grep -Fq 'DISPLAY/PLANNING GATE ONLY; NO FLIGHT CONTROL AUTHORITY' "$PLANNER" || {
  echo "STOP: planner authority boundary missing" >&2
  exit 24
}

grep -Fq 'internal sealed class AERISApproachRegistry' "$REGISTRY" || {
  echo "STOP: approach registry missing" >&2
  exit 25
}

grep -Fq 'TryGetChunkId' "$DB" || {
  echo "STOP: preload DB indexed read path missing" >&2
  exit 26
}

grep -Fq 'CloneImmutable' "$DB" || {
  echo "STOP: immutable preload DB decode/cache path missing" >&2
  exit 27
}

grep -Fq 'no AP or FlightCtrlState authority granted.' "$FOUNDATION" || {
  echo "STOP: LAND control-free authority marker missing" >&2
  exit 28
}

echo "AERIS54_LAND_R2_START_AUDIT=PASS"
echo "terrain_authority=ACCEPTED_PRELOAD_PTC_DB_CACHE"
echo "normal_sync_pqs=FORBIDDEN"
echo "corridor_snapshot=IMMUTABLE_BY_CONVENTION"
echo "corridor_incomplete=FAIL_CLOSED_PENDING"
echo "terrain_only_obstacle_completeness=FORBIDDEN"
echo "worker_rule=PURE_COMPUTE_NO_MUTABLE_UNITY_KSP_OBJECTS"
echo "publication_rule=MAIN_THREAD_IMMUTABLE_PUBLICATION"
echo "land_control_authority=NONE_PILOT"
echo "AERIS_CURRENT_STAGE=LAND_R2_READY_FOR_IMPLEMENTATION"
echo "next_action=Implement the immutable terrain corridor producer without changing accepted flight-control or terrain authority."
