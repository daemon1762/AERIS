#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
N = ROOT / 'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS28 REV3.5 SALBUTAMOL SULFATE R012 COLD START PRELOAD READY RECOVERY]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'
R012 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R012_COLD_START_PRELOAD_READY_RECOVERY'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, O, P, N, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
preload = P.read_text()
nav = N.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R010 not in renderer:
    fail('R010 generated parent required before R012 overlay')
if '[OH_REV3_5_R011_TURN_CHURN]' not in observer:
    fail('R011 observer source required before R012 overlay')
for token in ('ndReloadGeneration++;', 'frontReloadGeneration = ndReloadGeneration;',
              'if (Reloading) return false;', 'oh_nd_reload='):
    if token not in renderer:
        fail('black-reload successor contract missing before R012 overlay: ' + token)

# R012-A: Flight must not revoke a completed non-Flight preload target merely because
# the live current-position point moves. Keep the latest point snapshot, but defer the
# expensive completion/generation invalidation until the first non-Flight Tick. Repeated
# Flight updates coalesce into one reevaluation of the newest point set.
field_old = '''        bool stateDirty;\n        bool flightSuspended;\n        bool operationInFlight;\n'''
field_new = '''        bool stateDirty;\n        bool flightSuspended;\n        // R012: live Flight point updates are remembered immediately, while automatic\n        // completion invalidation is coalesced until non-Flight terrain production resumes.\n        bool deferredPointSetInvalidation;\n        bool operationInFlight;\n'''
preload, _ = replace_once(preload, field_old, field_new,
                          'R012 deferred point invalidation field')

points_old = '''                pointSetSignature = signature;\n                points.Clear();\n                points.AddRange(next);\n                foreach (BodyPlan plan in plans.Values)\n                {\n                    plan.PointCursor = 0;\n                    plan.PointScannedWithoutMiss = 0;\n                    plan.EstimatedTargetTiles = 0L;\n                    InvalidateAutomaticCompletion(plan);\n                    plan.Generation++;\n                }\n                stateDirty = true;\n'''
points_new = '''                pointSetSignature = signature;\n                points.Clear();\n                points.AddRange(next);\n                // R012: Flight has strict read priority and cannot service preload work.\n                // Do not turn a genuinely completed preload status into 99.x% merely\n                // because the moving current-position point changes while production is\n                // suspended. Preserve the newest points and reevaluate once on return to\n                // a non-Flight scene. HighLogic covers the first Flight frame before\n                // Tick() has had a chance to latch flightSuspended.\n                if (flightSuspended || HighLogic.LoadedSceneIsFlight)\n                {\n                    deferredPointSetInvalidation = true;\n                    stateDirty = true;\n                    return;\n                }\n                ApplyPointSetInvalidationLocked();\n'''
preload, _ = replace_once(preload, points_old, points_new,
                          'R012 Flight point update deferral')

helper_anchor = '''        static int ComparePreloadPoints(AERISTerrainPreloadPoint a,\n            AERISTerrainPreloadPoint b)\n'''
helper_new = '''        void ApplyPointSetInvalidationLocked()\n        {\n            foreach (BodyPlan plan in plans.Values)\n            {\n                plan.PointCursor = 0;\n                plan.PointScannedWithoutMiss = 0;\n                plan.EstimatedTargetTiles = 0L;\n                InvalidateAutomaticCompletion(plan);\n                plan.Generation++;\n            }\n            deferredPointSetInvalidation = false;\n            stateDirty = true;\n        }\n\n        void ApplyDeferredPointSetInvalidation()\n        {\n            lock (sync)\n            {\n                if (!deferredPointSetInvalidation) return;\n                ApplyPointSetInvalidationLocked();\n            }\n        }\n\n''' + helper_anchor
preload, _ = replace_once(preload, helper_anchor, helper_new,
                          'R012 coalesced point invalidation helpers')

nonflight_old = '''            }\n            flightSuspended = false;\n            ApplyStandardSchedulerState(true);\n'''
nonflight_new = '''            }\n            // R012: the latest Flight point set is now actionable. Apply exactly one\n            // completion/generation invalidation before non-Flight production resumes.\n            ApplyDeferredPointSetInvalidation();\n            flightSuspended = false;\n            ApplyStandardSchedulerState(true);\n'''
preload, _ = replace_once(preload, nonflight_old, nonflight_new,
                          'R012 non-Flight deferred invalidation apply')

# R012-B: the renderer already owns exact FRONT/READY authority. The ND standby surface
# must therefore communicate an unavailable/reloading presentation, not masquerade as a
# valid all-water map. Complete or latched FRONT textures still draw over this backdrop.
partial_old = '''                DrawLabel(plot, "TERRAIN GPU BUILDING " + percent + "%", centerStyle,\n                    new Color(0.58f, 0.76f, 0.82f, 1f));\n'''
partial_new = '''                DrawLabel(plot, "RELOADING ND " + percent + "%", centerStyle,\n                    new Color(0.58f, 0.76f, 0.82f, 1f));\n'''
nav, _ = replace_once(nav, partial_old, partial_new,
                      'R012 explicit ND reload label')

standby_old = '''        static void DrawTerrainStandbyBackground(Rect rect)\n        {\n            // Gate 5 Candidate 2: an explicit OFF->ON rebuild may legitimately have no\n            // reusable GPU FRONT because Terrain OFF must release presentation resources.\n            // Use the normal water-map background instead of flashing the near-black LAND\n            // focus background while the first exact FRONT is rebuilt.\n            FillRect(rect, new Color(0.025f, 0.145f, 0.285f, 1f));\n        }\n'''
standby_new = '''        static void DrawTerrainStandbyBackground(Rect rect)\n        {\n            // R012 cold-start recovery: a blue water-map surface is valid cartography and\n            // must never stand in for an uncommitted requested view. Until an exact or\n            // continuity FRONT is presented, use the explicit near-black reload backdrop.\n            // The renderer's complete-coverage and FRONT publication authority is unchanged.\n            FillRect(rect, new Color(0.015f, 0.025f, 0.035f, 1f));\n        }\n'''
nav, _ = replace_once(nav, standby_old, standby_new,
                      'R012 cold-start black standby backdrop')

r011_var = 'REV3_5_R011_VARIANT="' + R011 + '"\n'
r012_var = r011_var + 'REV3_5_R012_VARIANT="' + R012 + '"\n'
build, _ = replace_once(build, r011_var, r012_var,
                          'R012 build identity variable')

r011_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r011_turning_view_churn_observer.py"\n'
r012_verify = r011_verify + 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py"\n'
build, _ = replace_once(build, r011_verify, r012_verify,
                          'R012 build verifier')

r011_identity = 'printf \'rev3_5_r011_variant=%s\\n\' "$REV3_5_R011_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
r012_identity = r011_identity + 'printf \'rev3_5_r012_variant=%s\\n\' "$REV3_5_R012_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
build, _ = replace_once(build, r011_identity, r012_identity,
                          'R012 candidate identity')

r011_suite = " ('OH REV3.5 R011 Turning View Churn Observer','selftest_v01800_oh_rev35_r011_turning_view_churn_observer.py'),\n"
r012_suite = r011_suite + " ('OH REV3.5 R012 Cold Start Preload Ready Recovery','selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py'),\n"
prebuild, _ = replace_once(prebuild, r011_suite, r012_suite,
                           'R012 prebuild suite')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    if forbidden in preload or forbidden in nav:
        fail('rejected mechanism present in R012 modified source: ' + forbidden)

P.write_text(preload)
N.write_text(nav)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent_r010=' + R010)
print('observer_r011=' + R011)
print('r012=' + R012)
print('preload_flight_point_updates=latest snapshot retained; invalidation deferred/coalesced')
print('preload_nonflight_resume=one deferred completion reevaluation before production resumes')
print('nd_cold_start=near-black standby + explicit RELOADING ND label')
print('renderer_change=0 worker_change=0 scheduler_change=0 rasterizer_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 publication_authority_change=0')
