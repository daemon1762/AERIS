#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPECTED_BRANCH="agent/aeris50-new-nav-architecture-audit"
AERIS49_ACCEPTED="648c94bd83fee37b4d12fb4299ae61be542793bd"

BOOT="$ROOT/Source/AERISFlightControl/Core/AERISBootstrap.cs"
ND="$ROOT/Source/AERISFlightControl/UI/AERISNavigationDisplay.cs"
API="$ROOT/Source/AERISFlightControl/API/AERISExternalAutomationApi.cs"
LAND="$ROOT/Source/AERISFlightControl/Landing/AERISLandingFoundation.cs"
PLANS="$ROOT/Source/AERISFlightControl/FlightPlans/AERISFlightPlanLibrary.cs"

cd "$ROOT"

echo "=== AERIS50 / NEW_NAV ARCHITECTURE AUDIT ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "accepted_terrain_nd=$AERIS49_ACCEPTED"

fail=0
pass(){ echo "PASS $1"; }
bad(){ echo "FAIL $1"; fail=$((fail+1)); }

[[ "$(git branch --show-current)" = "$EXPECTED_BRANCH" ]] && pass "branch" || bad "branch"
git merge-base --is-ancestor "$AERIS49_ACCEPTED" HEAD && pass "AERIS49 accepted base" || bad "AERIS49 accepted base"
[[ -z "$(git status --porcelain)" ]] && pass "worktree clean" || bad "worktree clean"

grep -Fq 'navigation ARM rejected: legacy NAV remains removed.' "$BOOT" &&
  pass "legacy NAV control remains removed" || bad "legacy NAV control remains removed"
grep -Fq 'INDEPENDENT LAND IS SEPARATE' "$BOOT" &&
  pass "LAND remains independent from NAV" || bad "LAND remains independent from NAV"
grep -Fq 'return false;' "$BOOT" &&
  pass "legacy NAV arm fail-closed marker" || bad "legacy NAV arm fail-closed marker"

grep -Fq 'This class never writes FlightCtrlState or arms an AP director.' "$ND" &&
  pass "ND remains display-only" || bad "ND remains display-only"
grep -Fq 'Independent LAND' "$ND" &&
  pass "ND LAND overlay remains presentation-only boundary" || bad "ND LAND overlay remains presentation-only boundary"

grep -Fq 'Navigation and AutoLanding are reserved but' "$API" &&
  pass "automation Contract v2 NAV slot preserved/reserved" || bad "automation Contract v2 NAV slot preserved/reserved"
grep -Fq 'unavailable after the legacy NAV removal' "$API" &&
  pass "automation NAV runtime remains unavailable" || bad "automation NAV runtime remains unavailable"

grep -Fq 'ControlText { get { return "PILOT"; } }' "$LAND" &&
  pass "LAND foundation has no flight-control ownership" || bad "LAND foundation has no flight-control ownership"
grep -Fq 'return Landing.TryArm' "$BOOT" &&
  pass "LAND arm path remains separate" || bad "LAND arm path remains separate"

grep -Fq 'Data-only compatibility model.' "$PLANS" &&
  pass "flight-plan schema retained as data-only compatibility model" || bad "flight-plan schema retained as data-only compatibility model"
grep -Fq 'has no control, guidance, sequencing, landing or AP-write behavior.' "$PLANS" &&
  pass "flight-plan library has no current guidance authority" || bad "flight-plan library has no current guidance authority"

grep -Fq 'BANK/HDG/PITCH/V/S/ALT/ACC/VEL' "$BOOT" &&
  pass "accepted normal AP modes retained" || bad "accepted normal AP modes retained"

echo
echo "=== AERIS50 NEW_NAV BASELINE ==="
echo "terrain_nd=ACCEPTED"
echo "legacy_nav=REMOVED"
echo "nd_authority=DISPLAY_ONLY"
echo "land_authority=INDEPENDENT_PRESENTATION_FOUNDATION"
echo "flight_plan_authority=DATA_ONLY"
echo "automation_contract_v2_nav_slot=RESERVED_UNAVAILABLE"
echo "normal_ap_authority=BANK_HDG_PITCH_VS_ALT_ACC_VEL"
echo "new_nav_must_not_restore_legacy_nav=true"
echo "new_nav_must_not_write_from_nd=true"
echo "new_nav_must_not_merge_into_land=true"
echo "new_nav_must_preserve_contract_v2_slot=true"

echo
echo "checks_failed=$fail"
if (( fail != 0 )); then
  echo "AERIS50_NEW_NAV_ARCHITECTURE_AUDIT=FAIL"
  echo "AERIS_CURRENT_STAGE=NEW_NAV_ARCHITECTURE_AUDIT_FAIL"
  exit 40
fi

echo "AERIS50_NEW_NAV_ARCHITECTURE_AUDIT=PASS"
echo "AERIS_CURRENT_STAGE=NEW_NAV_ARCHITECTURE_AUDIT_PASS"
echo "next_action=Recover the historical NAV intent and define the new NAV authority/state machine without changing ND, LAND, accepted AP laws, or Contract v2 numbering."
