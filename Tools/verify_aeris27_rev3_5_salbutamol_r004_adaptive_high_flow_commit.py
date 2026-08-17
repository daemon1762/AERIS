#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R004 VERIFY]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
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
for marker, label in ((R001, 'R001 parent retained'),
                      (R002, 'R002 parent retained'),
                      (R003, 'R003 parent retained'),
                      (R004, 'R004 marker present')):
    check(marker in renderer, label)
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in renderer,
      'high-flow ceiling is exactly 2.00 ms')
check('Rev35R004FrameGuardMediumMilliseconds = 15.0' in renderer and
      'Rev35R004FrameGuardSoftMilliseconds = 20.0' in renderer and
      'Rev35R004FrameGuardHardMilliseconds = 25.0' in renderer,
      'frame guard thresholds are 15/20/25 ms')
check('Rev35R004PrepareChunkMedium = 128' in renderer and
      'Rev35R004PrepareChunkHigh = 256' in renderer,
      'adaptive prepare chunks are 64/128/256')
check('ResolveRev35R004CommitBudget(steadyCommitProfile)' in renderer,
      'pump uses adaptive budget resolver')
check('Time.unscaledDeltaTime * 1000.0' in renderer,
      'adaptive flow is guarded by host real-frame time')
check('backlog >= 24 || generationLag >= 8L' in renderer and
      'backlog >= 12 || generationLag >= 4L' in renderer and
      'backlog >= 4 || generationLag >= 2L' in renderer,
      'flow tiers respond to backlog and generation lag')
check(renderer.count('int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);') == 2,
      'source and packed loops resolve adaptive chunk width')
check('(iterations % Rev35PrepareChunkItems) == 0' not in renderer,
      'R001/R002 prepare loops no longer use fixed 64-item cadence')
check(renderer.count('operationHealthRev35R004AllocationContinues++;') == 3,
      'all three R002 allocation stops are budget-aware')
check('pending.PackedSource = new Vector3[count];' in renderer and
      'pending.PackedColours = new Color32[count];' in renderer and
      'pending.PackedIndices = new int[count];' in renderer,
      'R002 split allocation identity remains')
for token in ('oh_rev35_r004_variant=',
              'oh_rev35_r004_budget_050=',
              'oh_rev35_r004_budget_100=',
              'oh_rev35_r004_budget_150=',
              'oh_rev35_r004_budget_200=',
              'oh_rev35_r004_frame_guard=',
              'oh_rev35_r004_alloc_continue=',
              'oh_rev35_r004_budget_max_ms=',
              'oh_rev35_r004_chunk_max_items='):
    check(token in renderer, 'telemetry output ' + token)
check('REV3_5_R004_VARIANT="' + R004 + '"' in build,
      'build R004 identity present')
check('verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py' in build,
      'build invokes R004 verifier')
check('rev3_5_r004_variant=%s' in build,
      'candidate identity records R004')
check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz retained')
check('FinalizePendingEntryCommit(pending, system)' in renderer,
      'Finalize-only publication retained')
check('YieldPendingEntryCommit(executedStage, stageStart, true)' in renderer,
      'REV003 three-argument yield contract retained')
check('pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' in renderer,
      'REV003 mesh-authority successor retained')
check('Rev35R003MaximumStaleSkipsPerWindow = 8' in renderer and
      'operationHealthRev35R003StaleCompletedSkips++' in renderer,
      'R003 requested-view anti-HOL admission retained')
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
