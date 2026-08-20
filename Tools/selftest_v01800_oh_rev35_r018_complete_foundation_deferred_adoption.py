#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O17 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r018_complete_foundation_deferred_adoption.py'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'

R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_COMPLETE_FOUNDATION_DEFERRED_ADOPTION'

checks = []
def ck(ok, label):
    checks.append((bool(ok), label))

for path in (R, O17, A, B, PRE):
    ck(path.is_file(), 'file exists: ' + str(path.relative_to(ROOT)))

if not all(path.is_file() for path in (R, O17, A, B, PRE)):
    for ok, label in checks:
        print(('[PASS] ' if ok else '[FAIL] ') + label)
    raise SystemExit(1)

r = R.read_text()
o = O17.read_text()
a = A.read_text()
b = B.read_text()
pre = PRE.read_text()

ck(R018 in r, 'R018 renderer identity present')
ck(R017 in o and '[OH_REV3_5_R017_ND_PRESENT_STALL]' in o,
   'R017 stall observer retained')
ck('ContentPlanningHeadingStepDeg = 6f' in r,
   'formal cumulative 6 degree planning threshold retained')

ck('rev35R018DeferredAdoptionPending' in r,
   'deferred-adoption state present')
ck('readonly HashSet<string> rev35R018ProtectedActiveKeys' in r,
   'ACTIVE protection stores cache-key strings only')
ck('Rev35R018NeedsDeferredTargetRefresh' in r and
   'ContentPlanningHeadingStepDeg' in r,
   'pending target uses inherited planning threshold')
ck('rev35R018DeferredAdoptionPending ?' in r and
   'rev35R018TargetHeadingDeg : mapHeadingDeg' in r,
   'candidate capture uses stable threshold-qualified target')
ck('!requestedViewReady || rev35R018DeferredAdoptionPending' in r,
   'pending handover retries on inherited content cadence')

candidate_gate = '''bool rev35R018CandidateComplete =
                        visible.FoundationComplete &&
                        rev35R018CandidateCoverage >= 0.999f &&
                        readyFar >= visible.FarFoundationCount;'''
ck(candidate_gate in r, 'candidate adoption requires exact complete foundation gate')
ck('contentVisible = visible;' in r and
   'if (!rev35R018DeferredAdoptionPending ||' in r and
   'rev35R018CandidateComplete)' in r,
   'content authority publication is conditional')
ck('Rev35R018RestoreActivePresentationScratch(' in r,
   'incomplete candidate restores ACTIVE presentation scratch')
ck('operationHealthRev35R018ActiveRestoreSafetyBlock' in r,
   'ACTIVE restore has fail-closed safety telemetry')

ck('rev35R018ProtectedActiveKeys.Contains(entry.CacheKey)' in r,
   'superseded/LRU path protects ACTIVE entry keys')
ck('rev35R018ProtectedActiveKeys.Contains(pair.Key)' in r,
   'RenderReady prune protects ACTIVE keys')
ck('Rev35R018ClearDeferredAdoption();\n            contentVisible = null;' in r,
   'hard content reset clears deferred ownership')

swap_gate = '''foundationComplete = rendered && visible.FoundationComplete &&
                    lastBackFoundationCoverage >= 0.999f &&
                    readyFar >= visible.FarFoundationCount;'''
ck(swap_gate in r, 'existing FRONT swap complete-coverage gate unchanged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in r,
   'fixed 10Hz authoritative presentation unchanged')

for field in (
    'oh_rev35_r018_pending=',
    'oh_rev35_r018_handover_requested=',
    'oh_rev35_r018_handover_retargeted=',
    'oh_rev35_r018_handover_deferred=',
    'oh_rev35_r018_handover_ready=',
    'oh_rev35_r018_handover_adopted=',
    'oh_rev35_r018_active_restore=',
    'oh_rev35_r018_active_safety_block=',
    'oh_rev35_r018_protected_superseded_skip=',
    'oh_rev35_r018_protected_prune_skip='):
    ck(field in r, 'telemetry field: ' + field)

ck(('REV3_5_R018_VARIANT="' + R018 + '"') in b,
   'R018 build identity wired')
ck('verify_aeris29_rev3_5_salbutamol_r018_complete_foundation_deferred_adoption.py' in b,
   'R018 build verifier wired')
ck('rev3_5_r018_variant=' in b, 'R018 candidate identity wired')
ck('selftest_v01800_oh_rev35_r018_complete_foundation_deferred_adoption.py' in pre,
   'R018 prebuild selftest wired')

ck(R013 not in r and 'REV3_5_R013_VARIANT=' not in b and
   'rev3_5_r013_variant=' not in b,
   'rejected R013 remains absent')

ck("AERISTerrainGpuTileRenderer.cs" in a, 'applicator targets renderer')
for forbidden_path in (
    'AERISWorkerScheduler.cs',
    'AERISTerrainGpuTileRasterizer.cs',
    'AERISTerrainPreloadBuilder.cs',
    'AERISTerrainPreloadDatabase.cs',
    'AERISNavigationDisplay.cs',
    'AERISFlightControl.cs',
    'AERISFlightDataRecorder.cs'):
    ck(forbidden_path not in a, 'applicator does not target ' + forbidden_path)

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
    'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    ck(forbidden not in a, 'applicator avoids forbidden mechanism: ' + forbidden)

for forbidden in ('new RenderTexture', 'new Mesh', 'pendingEntryCommit2',
                  'second staged', 'speculative'):
    ck(forbidden not in a, 'no R018 duplicate/speculative authority: ' + forbidden)

failed = [label for ok, label in checks if not ok]
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
print('R018 selftest %d/%d' % (len(checks) - len(failed), len(checks)))
if failed:
    raise SystemExit(1)
