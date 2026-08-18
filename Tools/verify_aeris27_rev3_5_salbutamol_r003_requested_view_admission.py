#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R003 VERIFY]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
checks = []


def check(ok, label):
    ok = bool(ok)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


check(R.is_file(), 'renderer exists')
check(B.is_file(), 'build exists')
if not R.is_file() or not B.is_file():
    raise SystemExit(1)
renderer = R.read_text()
build = B.read_text()
hf4_descendant = HF4 in renderer
check(R001 in renderer, 'R001 parent retained')
check(R002 in renderer, 'R002 parent retained')
check(R003 in renderer, 'R003 marker present')
check('Rev35R003MaximumStaleSkipsPerWindow = 8' in renderer,
      'stale skip window bounded at 8')
check('requested.Count > 0 &&\n                    !requested.Contains(pendingEntryCommit.CacheKey)' in renderer,
      'pending commit is gated by latest requested viewport')
check(renderer.count('CancelPendingEntryCommit();') >= 4,
      'stale commit cancellation integrated without removing lifecycle cancels')
check('operationHealthRev35R003StalePendingCancels++' in renderer,
      'stale pending cancellation telemetry')
check('operationHealthRev35R003StaleCompletedSkips++' in renderer,
      'stale completed admission-skip telemetry')
check('operationHealthRev35R003RelevantAdmissions++' in renderer,
      'relevant admission telemetry')
for token in ('oh_rev35_r003_variant=',
              'oh_rev35_r003_stale_pending_cancel=',
              'oh_rev35_r003_stale_completed_skip=',
              'oh_rev35_r003_relevant_admit='):
    check(token in renderer, 'telemetry output ' + token)
check('REV3_5_R003_VARIANT="' + R003 + '"' in build,
      'build R003 identity present')
check('verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py' in build,
      'build invokes R003 verifier')
check('rev3_5_r003_variant=%s' in build,
      'candidate identity records R003')
check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz witness retained')
check('FinalizePendingEntryCommit(pending, system)' in renderer,
      'Finalize-only publication retained')
if hf4_descendant:
    check('pending.PackedSource = new Vector3[count];' in renderer and
          'AcquireRev35R006Hf4ColourBuffer(count)' in renderer and
          'AcquireRev35R006Hf4IndexBuffer(count)' in renderer and
          'pending.PackedColours = new Color32[count];' not in renderer and
          'pending.PackedIndices = new int[count];' not in renderer,
          'HF4 descendant retains R002 split stages with bounded colour/index acquire')
else:
    check('pending.PackedSource = new Vector3[count];' in renderer and
          'pending.PackedColours = new Color32[count];' in renderer and
          'pending.PackedIndices = new int[count];' in renderer,
          'R002 split allocation contract retained')
check('YieldPendingEntryCommit(executedStage, stageStart, true)' in renderer,
      'REV003 three-argument yield contract retained')
check('pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' in renderer,
      'REV003 mesh-authority successor retained')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation',
                  'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
                  'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in renderer, 'renderer excludes ' + forbidden)

failed = [label for ok, label in checks if not ok]
if failed:
    print(PREFIX + ' FAIL count=%d' % len(failed))
    raise SystemExit(1)
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
