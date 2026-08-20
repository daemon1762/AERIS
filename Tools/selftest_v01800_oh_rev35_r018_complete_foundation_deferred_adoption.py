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

R008 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
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
ck('const float ContentPlanningHeadingStepDeg = 6f;' in r and
   'if (headingDelta >= ContentPlanningHeadingStepDeg) return true;' in r,
   'formal cumulative 6 degree planning authority retained')

# R018 must be a successor of the exact current-FAR / continuous-commit / publication
# lineage, not a replacement implementation of those mechanisms.
ck(R008 in r and 'rasterizer.ReconcileCurrentRequests(requested);' in r,
   'R008 current-request reconciliation retained')
ck('for (int admissionPass = 0; admissionPass < 2; admissionPass++)' in r,
   'R008 FAR-first two-pass admission retained')
ck(R010 in r and 'rev35R007FoundationQueue.Count > 0' in r,
   'R010 continuous single commit stream retained')
ck(R014 in r and 'bool rev35R014ReconcileRequired = contentGeometryChanged ||' in r,
   'R014 publication-gated reconcile retained')
ck('rev35R014ReconciledPublicationSerial =' in r,
   'R014 reconciled publication serial retained')

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

ck('bool rev35R018CandidateComplete = visible.FoundationComplete &&' in r and
   'rev35R018CandidateCoverage >= 0.999f' in r and
   'readyFar >= visible.FarFoundationCount;' in r,
   'candidate adoption requires exact complete foundation gate')
ck('contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,' in r,
   'inherited foundation measurement authority retained')
ck('if (!rev35R018DeferredAdoptionPending ||' in r and
   'rev35R018CandidateComplete)' in r and
   'contentVisible = visible;' in r,
   'content authority publication is conditional')
ck('if (rev35R018DeferredAdoptionPending)' in r and
   'Rev35R018RestoreActivePresentationScratch(' in r,
   'incomplete candidate restores ACTIVE presentation scratch')
ck('operationHealthRev35R018ActiveRestoreSafetyBlock' in r,
   'ACTIVE restore has fail-closed safety telemetry')
ck(r.count('system.CaptureVisible(') == 1,
   'single CaptureVisible authority retained')

ck('rev35R018ProtectedActiveKeys.Contains(entry.CacheKey)' in r,
   'superseded/LRU path protects ACTIVE entry keys')
ck('rev35R018ProtectedActiveKeys.Contains(pair.Key)' in r,
   'RenderReady prune protects ACTIVE keys')
ck('Rev35R018ClearDeferredAdoption();\n            contentVisible = null;' in r,
   'hard content reset clears deferred ownership')

ck('foundationComplete = rendered && visible.FoundationComplete &&' in r and
   'lastBackFoundationCoverage >= 0.999f' in r and
   'readyFar >= visible.FarFoundationCount;' in r,
   'existing FRONT swap complete-coverage gate unchanged')
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

ck('AERISTerrainGpuTileRenderer.cs' in a, 'applicator targets renderer')
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
