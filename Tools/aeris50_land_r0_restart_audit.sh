#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris50_land_r0_restart_audit.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
ACCEPTED="648c94bd83fee37b4d12fb4299ae61be542793bd"
EXPECTED_BRANCH="agent/aeris50-land-r0-restart-audit"

cd "$ROOT"

failed=0
pass(){ echo "PASS $*"; }
fail(){ echo "FAIL $*"; failed=$((failed+1)); }
finding(){ echo "FINDING $*"; }
gap(){ echo "GAP $*"; }

contains(){
  local file="$1" pattern="$2" label="$3"
  if grep -Fq -- "$pattern" "$file"; then pass "$label"; else fail "$label"; fi
}
absent(){
  local file="$1" pattern="$2" label="$3"
  if grep -Fq -- "$pattern" "$file"; then fail "$label"; else pass "$label"; fi
}
regex_absent(){
  local file="$1" pattern="$2" label="$3"
  if grep -Eq -- "$pattern" "$file"; then fail "$label"; else pass "$label"; fi
}

BRANCH="$(git branch --show-current)"
HEAD="$(git rev-parse HEAD)"

echo "=== AERIS50 / LAND R0 RESTART AUDIT ==="
echo "branch=$BRANCH"
echo "HEAD=$HEAD"
echo "accepted_terrain_nd=$ACCEPTED"
echo "KSP=$KSP"
echo

[[ "$BRANCH" == "$EXPECTED_BRANCH" ]] && pass "LAND R0 audit branch" || fail "LAND R0 audit branch"
git merge-base --is-ancestor "$ACCEPTED" HEAD && pass "AERIS49 accepted base is ancestor" || fail "AERIS49 accepted base is ancestor"

SOURCE_DIFF="$(git diff --name-only "$ACCEPTED"..HEAD -- Source GameData || true)"
if [[ -z "$SOURCE_DIFF" ]]; then
  pass "R0 is source/GameData control-neutral"
else
  fail "R0 unexpectedly changes Source/GameData"
  printf '%s\n' "$SOURCE_DIFF"
fi

CS="Source/AERISFlightControl/AERISFlightControl.csproj"
BOOT="Source/AERISFlightControl/Core/AERISBootstrap.cs"
LAND="Source/AERISFlightControl/Landing/AERISLandingFoundation.cs"
APP_MODELS="Source/AERISFlightControl/Landing/AERISApproachModels.cs"
APP_PLAN="Source/AERISFlightControl/Landing/AERISAdaptiveApproachPlanner.cs"
APP_REG="Source/AERISFlightControl/Landing/AERISApproachRegistry.cs"
TRACK="Source/AERISFlightControl/Landing/AERISRunwayTrackToken.cs"
GROUND_API="Source/AERISFlightControl/API/AERISGroundAssistApi.cs"
GROUND="Source/AERISFlightControl/Protect/GroundStabilityProtection.cs"
ND="Source/AERISFlightControl/UI/AERISNavigationDisplay.cs"
WINDOW="Source/AERISFlightControl/UI/AERISWindow.cs"

for f in "$CS" "$BOOT" "$LAND" "$APP_MODELS" "$APP_PLAN" "$APP_REG" "$TRACK" "$GROUND_API" "$GROUND" "$ND" "$WINDOW"; do
  [[ -f "$f" ]] && pass "present: $f" || fail "present: $f"
done

contains "$CS" 'Landing\AERISLandingFoundation.cs' "LAND foundation compiled"
contains "$CS" 'Landing\AERISApproachModels.cs' "Approach models compiled"
contains "$CS" 'Landing\AERISAdaptiveApproachPlanner.cs' "Adaptive Approach planner compiled"
contains "$CS" 'Landing\AERISApproachRegistry.cs' "Approach registry compiled"
contains "$CS" 'Landing\AERISRunwayTrackToken.cs' "Runway Track Token compiled"
contains "$CS" 'API\AERISGroundAssistApi.cs' "Ground Assist API compiled"

contains "$BOOT" 'Airfields=new AERISAirfieldRegistry' "Airfield Registry instantiated"
contains "$BOOT" 'Landing=new AERISLandingFoundation(Airfields)' "LAND foundation instantiated"
contains "$BOOT" 'AERISRunwayTrackTokenApi.Bind' "Runway Track Token API bound"
absent "$BOOT" 'new AERISApproachRegistry' "Approach Registry remains disconnected from runtime"
absent "$BOOT" 'new AERISAdaptiveApproachPlanner' "Adaptive Approach planner remains non-runtime/static"

contains "$LAND" 'get { return "PILOT"; }' "LAND control owner remains PILOT"
contains "$LAND" 'ARMED / OBSERVATION ONLY / CONTROL REMAINS PILOT' "LAND ARM remains observation-only"
contains "$LAND" 'CAPTURE LOGIC NOT ENABLED IN FIRST GATE' "LAND capture control remains disabled"
contains "$LAND" 'TryCreateTrackToken' "LAND exposes immutable runway handoff token"
regex_absent "$LAND" 'FlightCtrlState[[:space:]]+[A-Za-z_]' "LAND foundation has no FlightCtrlState writer"
regex_absent "$LAND" '(^|[^A-Za-z])(mainThrottle|wheelThrottle|wheelSteer)[[:space:]]*=' "LAND foundation has no throttle/wheel writer"
absent "$LAND" 'OnFlyByWire(' "LAND foundation has no fly-by-wire callback"
absent "$LAND" 'SetArmed(' "LAND foundation does not directly arm AP directors"

contains "$ND" 'This class never writes FlightCtrlState or arms an AP director.' "ND LAND presentation declares display-only boundary"
regex_absent "$ND" 'FlightCtrlState[[:space:]]+[A-Za-z_]' "ND has no FlightCtrlState writer"
absent "$ND" 'OnFlyByWire(' "ND has no fly-by-wire callback"
contains "$WINDOW" 'ARM LAND — OBSERVE' "LAND UI remains observation-arm"
contains "$WINDOW" 'CONTROL REMAINS PILOT' "LAND UI exposes pilot authority"

contains "$APP_MODELS" 'MinimumGlideAngleDeg = 2.5' "Adaptive Glide minimum retained"
contains "$APP_MODELS" 'PreferredGlideAngleDeg = 3.0' "Adaptive Glide preferred 3 deg retained"
contains "$APP_MODELS" 'NormalMaximumGlideAngleDeg = 4.0' "Adaptive Glide normal maximum retained"
contains "$APP_MODELS" 'ObstacleMaximumGlideAngleDeg = 5.0' "Adaptive Glide obstacle maximum retained"
contains "$APP_MODELS" 'ConditionalMaximumGlideAngleDeg = 6.0' "Adaptive Glide conditional maximum retained"
contains "$APP_MODELS" 'MinimumFinalStraightMeters = 4000.0' "minimum stabilized final straight retained"
contains "$APP_MODELS" 'MissedApproach = 4' "missed-approach leg model retained"
contains "$APP_PLAN" 'FINAL LOCALIZER MUST COINCIDE WITH RUNWAY CENTERLINE' "final localizer centerline invariant retained"
contains "$APP_PLAN" 'AddMissedApproach' "planner retains missed-approach construction"
regex_absent "$APP_PLAN" 'FlightCtrlState[[:space:]]+[A-Za-z_]' "Adaptive Approach planner remains control-free"
absent "$APP_PLAN" 'SetArmed(' "Adaptive Approach planner cannot arm AP directors"

contains "$TRACK" 'future' "Runway Track Token remains future handoff contract"
contains "$GROUND_API" 'AERISRunwayTrackTokenApi' "Ground Assist API documents separate runway token contract"
contains "$GROUND" 'postTouchdownSessionActive' "Ground Assist retains touchdown-session machinery"
contains "$GROUND" 'ApplyGroundBrakeAssist' "Ground Assist retains rollout brake authority"
contains "$GROUND" 'UpdateGroundAssist' "Ground Assist retains airbrake handoff path"

if grep -Fq 'GlideAngleStepDeg = 0.25' "$APP_MODELS"; then
  finding "adaptive_glide_step_current_deg=0.25"
  finding "historical_AERIS13_target_deg=0.1"
else
  finding "adaptive_glide_step_current_deg=UNKNOWN"
fi

for pattern in 'SurfaceSpeed' 'Protect' 'RunwayTooShort' 'VerticalSpeed'; do
  if grep -Fq "$pattern" "$LAND"; then
    finding "LAND foundation capture gate contains $pattern"
  else
    gap "LAND foundation capture gate lacks $pattern integration"
  fi
done

if grep -Fq 'AERISRunwayTrackTokenApi' "$GROUND"; then
  finding "Ground Assist consumes runway track token"
else
  gap "Ground Assist does not yet consume runway track token"
fi

echo
echo "=== AERIS50 LAND R0 CLASSIFICATION ==="
echo "runway_registry=RUNTIME_CONNECTED"
echo "land_foundation=RUNTIME_CONNECTED_OBSERVATION_ONLY"
echo "nd_land_presentation=RUNTIME_CONNECTED_DISPLAY_ONLY"
echo "adaptive_approach_planner=COMPILED_DISCONNECTED"
echo "approach_registry=COMPILED_DISCONNECTED"
echo "runway_track_token=PUBLISHED_CONTROL_FREE"
echo "ground_assist=EXISTING_CONTROL_CAPABLE_POST_TOUCHDOWN"
echo "land_flight_control_authority=NONE"
echo "legacy_nav=REMOVED"
echo "new_nav=BLOCKED_UNTIL_LAND_ACCEPTED"
echo "checks_failed=$failed"

if (( failed != 0 )); then
  echo "AERIS50_LAND_R0_RESTART_AUDIT=FAIL"
  exit 1
fi

echo "AERIS50_LAND_R0_RESTART_AUDIT=PASS"
echo "AERIS_CURRENT_STAGE=LAND_R0_RESTART_AUDIT_PASS"
echo "next_action=LAND-R1 connect Approach Registry and Adaptive Approach as observation/display-only runtime data without granting control authority."
