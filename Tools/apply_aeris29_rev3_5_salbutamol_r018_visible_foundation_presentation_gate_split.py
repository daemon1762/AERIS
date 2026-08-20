#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O17 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
PLANNER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainViewportFoundationPlanner.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R018 VISIBLE FOUNDATION PRESENTATION GATE SPLIT]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, O17, PLANNER, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O17.read_text()
planner = PLANNER.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R014 not in renderer:
    fail('formal R014 generated renderer parent required before R018 overlay')
if R017 not in observer or '[OH_REV3_5_R017_ND_PRESENT_STALL]' not in observer:
    fail('R017 diagnostic parent required before R018 overlay')
if ('REV3_5_R017_VARIANT="' + R017 + '"') not in build:
    fail('R017 build identity parent required before R018 overlay')
if 'internal const int GuardRingTiles = 1;' not in planner or \
   'AERISNdMapProjection.Create(body,' not in planner:
    fail('canonical viewport-foundation planner contract missing')
if R013 in renderer or 'REV3_5_R013_VARIANT=' in build or 'rev3_5_r013_variant=' in build:
    fail('rejected R013 experiment must remain absent')

if R018 not in renderer:
    field_old = '''        long operationHealthRev35R017CadenceSkips;\n'''
    field_new = field_old + '''        // R018 separates hidden temporal-overscan preparation from the exact visible\n        // presentation gate. The canonical viewport planner is evaluated only during the\n        // existing full content reconcile and its exact-current FAR readiness is cached.\n        const string Rev35R018Variant = "''' + R018 + '''";\n        bool operationHealthRev35R018VisiblePlanValid;\n        int operationHealthRev35R018VisibleRequiredFar;\n        int operationHealthRev35R018VisibleReadyFar;\n        float operationHealthRev35R018VisibleCoverage;\n        int operationHealthRev35R018OverscanRequiredFar;\n        int operationHealthRev35R018OverscanReadyFar;\n        long operationHealthRev35R018OverscanHolAvoided;\n'''
    renderer, _ = replace_once(renderer, field_old, field_new,
                               'R018 identity/readiness telemetry fields')

    reset_old = '''            contentFoundationCoverage = 0f;\n            contentSnapshotValid = false;\n'''
    reset_new = '''            contentFoundationCoverage = 0f;\n            operationHealthRev35R018VisiblePlanValid = false;\n            operationHealthRev35R018VisibleRequiredFar = 0;\n            operationHealthRev35R018VisibleReadyFar = 0;\n            operationHealthRev35R018VisibleCoverage = 0f;\n            operationHealthRev35R018OverscanRequiredFar = 0;\n            operationHealthRev35R018OverscanReadyFar = 0;\n            contentSnapshotValid = false;\n'''
    renderer, _ = replace_once(renderer, reset_old, reset_new,
                               'R018 content-snapshot reset')

    helper_old = '''            int required = Math.Max(0, visible.FarFoundationCount);\n            int ready = Math.Min(required, readyFar);\n            return required <= 0 ? 0f : Mathf.Clamp01(ready / (float)required);\n        }\n\n        void SwapFrontAndBack(AERISTerrainVisibleTileSet visible, Vessel vessel,\n'''
    helper_new = '''            int required = Math.Max(0, visible.FarFoundationCount);\n            int ready = Math.Min(required, readyFar);\n            return required <= 0 ? 0f : Mathf.Clamp01(ready / (float)required);\n        }\n\n        // R018 exact visible-foundation readiness. Reuse the canonical Gate 3.1 planner\n        // rather than inventing a second geometry approximation. The planner already owns\n        // Track-Up rotation, 1.30 horizontal scale, lower-aircraft anchor and a one-tile\n        // guard ring. This method runs only inside R014 full content reconcile.\n        void MeasureVisibleFoundationGpuReadiness(CelestialBody body,\n            AERISTerrainHeightTile[] tiles, Entry[] currentEntries,\n            double centerLatitudeDeg, double centerLongitudeDeg,\n            float visibleRangeMeters, float mapHeadingDeg, bool trackUp,\n            float anchorV, AERISTerrainRenderTargetOrientation orientation,\n            out bool planValid, out int requiredFar, out int readyFar)\n        {\n            planValid = false;\n            requiredFar = 0;\n            readyFar = 0;\n            if (body == null || tiles == null) return;\n\n            string environmentHash = string.Empty;\n            for (int i = 0; i < tiles.Length; i++)\n            {\n                AERISTerrainHeightTile tile = tiles[i];\n                if (tile == null || string.IsNullOrEmpty(tile.Key.EnvironmentHash))\n                    continue;\n                environmentHash = tile.Key.EnvironmentHash;\n                break;\n            }\n            if (string.IsNullOrEmpty(environmentHash)) return;\n\n            AERISTerrainViewportFoundationPlan plan =\n                AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,\n                    centerLatitudeDeg, centerLongitudeDeg, visibleRangeMeters,\n                    mapHeadingDeg, trackUp, anchorV, orientation);\n            if (plan == null || plan.FarKeys == null || plan.FarKeys.Length <= 0)\n                return;\n\n            requiredFar = plan.FarKeys.Length;\n            planValid = true;\n            for (int requiredIndex = 0; requiredIndex < plan.FarKeys.Length;\n                 requiredIndex++)\n            {\n                AERISTerrainTileKey requiredKey = plan.FarKeys[requiredIndex];\n                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)\n                {\n                    AERISTerrainHeightTile tile = tiles[tileIndex];\n                    if (tile == null || !tile.Key.Equals(requiredKey)) continue;\n                    Entry current = currentEntries != null &&\n                        tileIndex < currentEntries.Length ?\n                        currentEntries[tileIndex] : null;\n                    if (current != null && current.CoverageFraction >= 0.999f)\n                        readyFar++;\n                    break;\n                }\n            }\n        }\n\n        void SwapFrontAndBack(AERISTerrainVisibleTileSet visible, Vessel vessel,\n'''
    renderer, _ = replace_once(renderer, helper_old, helper_new,
                               'R018 canonical visible FAR readiness helper')

    reconcile_old = '''                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,\n                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);\n                contentVisible = visible;\n'''
    reconcile_new = '''                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,\n                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);\n                MeasureVisibleFoundationGpuReadiness(vessel.mainBody, tiles,\n                    currentEntriesScratch, centerLatitudeDeg, centerLongitudeDeg,\n                    rangeMeters, mapHeadingDeg, trackUp, anchorV, orientation,\n                    out operationHealthRev35R018VisiblePlanValid,\n                    out operationHealthRev35R018VisibleRequiredFar,\n                    out operationHealthRev35R018VisibleReadyFar);\n                operationHealthRev35R018VisibleCoverage =\n                    operationHealthRev35R018VisiblePlanValid &&\n                    operationHealthRev35R018VisibleRequiredFar > 0 ?\n                    Mathf.Clamp01(operationHealthRev35R018VisibleReadyFar /\n                        (float)operationHealthRev35R018VisibleRequiredFar) : 0f;\n                contentVisible = visible;\n'''
    renderer, _ = replace_once(renderer, reconcile_old, reconcile_new,
                               'R018 visible readiness inside full reconcile')

    gate_prep_old = '''            bool refreshAllowed = ShouldRefreshBackBuffer(visible, refreshRequired);\n            bool rendered = false;\n'''
    gate_prep_new = '''            bool refreshAllowed = ShouldRefreshBackBuffer(visible, refreshRequired);\n\n            bool r018VisibleGpuComplete =\n                operationHealthRev35R018VisiblePlanValid &&\n                operationHealthRev35R018VisibleRequiredFar > 0 &&\n                operationHealthRev35R018VisibleReadyFar >=\n                    operationHealthRev35R018VisibleRequiredFar;\n            bool r018OverscanGpuComplete = visible.FoundationComplete &&\n                lastBackFoundationCoverage >= 0.999f &&\n                readyFar >= visible.FarFoundationCount;\n            operationHealthRev35R018OverscanRequiredFar =\n                Math.Max(0, visible.FarFoundationCount);\n            operationHealthRev35R018OverscanReadyFar =\n                Math.Min(operationHealthRev35R018OverscanRequiredFar,\n                    Math.Max(0, readyFar));\n\n            bool rendered = false;\n'''
    renderer, _ = replace_once(renderer, gate_prep_old, gate_prep_new,
                               'R018 split presentation readiness')

    gate_old = '''                foundationComplete = rendered && visible.FoundationComplete &&\n                    lastBackFoundationCoverage >= 0.999f &&\n                    readyFar >= visible.FarFoundationCount;\n                if (foundationComplete)\n                {\n                    SwapFrontAndBack(visible, vessel, centerLatitudeDeg,\n'''
    gate_new = '''                foundationComplete = rendered && r018VisibleGpuComplete;\n                if (foundationComplete)\n                {\n                    if (!r018OverscanGpuComplete)\n                        operationHealthRev35R018OverscanHolAvoided++;\n                    SwapFrontAndBack(visible, vessel, centerLatitudeDeg,\n'''
    renderer, _ = replace_once(renderer, gate_old, gate_new,
                               'R018 FRONT swap visible gate')

    recovery_old = '''            bool readyFoundationNow = visible.FoundationComplete &&\n                lastBackFoundationCoverage >= 0.999f &&\n                readyFar >= visible.FarFoundationCount;\n'''
    recovery_new = '''            bool readyFoundationNow = r018VisibleGpuComplete;\n'''
    renderer, _ = replace_once(renderer, recovery_old, recovery_new,
                               'R018 recovery visible gate')

    telemetry_old = (
        '                "; oh_rev35_r014_retry_reconcile=" + '
        'operationHealthRev35R014RetryReconciles +\n')
    telemetry_new = telemetry_old + (
        '                "; oh_rev35_r018_variant=" + Rev35R018Variant +\n'
        '                "; oh_rev35_r018_visible_plan_valid=" + '
        '(operationHealthRev35R018VisiblePlanValid ? 1 : 0) +\n'
        '                "; oh_rev35_r018_visible_required_far=" + '
        'operationHealthRev35R018VisibleRequiredFar +\n'
        '                "; oh_rev35_r018_visible_ready_far=" + '
        'operationHealthRev35R018VisibleReadyFar +\n'
        '                "; oh_rev35_r018_visible_coverage=" + '
        'operationHealthRev35R018VisibleCoverage.ToString("F3", '
        'CultureInfo.InvariantCulture) +\n'
        '                "; oh_rev35_r018_overscan_required_far=" + '
        'operationHealthRev35R018OverscanRequiredFar +\n'
        '                "; oh_rev35_r018_overscan_ready_far=" + '
        'operationHealthRev35R018OverscanReadyFar +\n'
        '                "; oh_rev35_r018_overscan_hol_avoided=" + '
        'operationHealthRev35R018OverscanHolAvoided +\n')
    renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                               'R018 runtime telemetry')
else:
    print(PREFIX + ' renderer overlay already present')

r017_var = 'REV3_5_R017_VARIANT="' + R017 + '"\n'
r018_var = r017_var + 'REV3_5_R018_VARIANT="' + R018 + '"\n'
build, _ = replace_once(build, r017_var, r018_var,
                        'R018 build identity variable')

r017_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py"\n')
r018_verify = r017_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py"\n')
build, _ = replace_once(build, r017_verify, r018_verify,
                        'R018 build verifier')

r017_identity = (
    'printf \'rev3_5_r017_variant=%s\\n\' "$REV3_5_R017_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r018_identity = r017_identity + (
    'printf \'rev3_5_r018_variant=%s\\n\' "$REV3_5_R018_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r017_identity, r018_identity,
                        'R018 candidate identity')

r017_suite = (
    " ('OH REV3.5 R017 ND Presentation Stall Observer',"
    "'selftest_v01800_oh_rev35_r017_nd_presentation_stall_observer.py'),\n")
r018_suite = r017_suite + (
    " ('OH REV3.5 R018 Visible Foundation Presentation Gate Split',"
    "'selftest_v01800_oh_rev35_r018_visible_foundation_presentation_gate_split.py'),\n")
prebuild, _ = replace_once(prebuild, r017_suite, r018_suite,
                           'R018 prebuild suite')

if R013 in renderer or 'REV3_5_R013_VARIANT=' in build or \
   'rev3_5_r013_variant=' in build:
    fail('R013 build/runtime wiring leaked into R018')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    if forbidden in renderer:
        fail('forbidden mechanism present after R018: ' + forbidden)

R.write_text(renderer)
B.write_text(build)
PRE.write_text(prebuild)

print(PREFIX + ' APPLY PASS')
print('parent_r014=' + R014)
print('parent_r017=' + R017)
print('r018=' + R018)
print('visible_gate=canonical viewport foundation planner at exact user-visible range')
print('visible_guard=existing one-tile Gate3.1 guard ring retained')
print('visible_measurement=full content reconcile only; 10Hz motion path allocation unchanged')
print('overscan_preparation=UNCHANGED 1.35x/250km inherited authority')
print('front_swap=rendered && exact-current visible FAR plan complete')
print('overscan_cpu_gpu_hol=removed from presentation admission; telemetry retained')
print('recovery_gate=uses same cached visible FAR readiness')
print('quality_change=0 10Hz_change=0 exact_range_change=0 worker_change=0')
print('ap_change=0 fbw_change=0 protect_change=0 publication_authority_change=0')
