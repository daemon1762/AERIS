#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R005 VERIFY]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
R005 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'
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
                      (R004, 'R004 parent retained'),
                      (R005, 'R005 marker present')):
    check(marker in renderer, label)

check('const int Rev35R005SourceChunkHardCap = 64;' in renderer,
      'source/geographic heavy lane hard cap is exactly 64')
check('Rev35R004PrepareChunkMedium = 128' in renderer and
      'Rev35R004PrepareChunkHigh = 256' in renderer,
      'packed lightweight lane retains R004 64/128/256 capacity')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in renderer and
      'ResolveRev35R004CommitBudget(steadyCommitProfile)' in renderer,
      'R004 adaptive 0.50..2.00 ms commit budget retained')
check('Time.unscaledDeltaTime * 1000.0' in renderer,
      'R004 host-frame guard retained')

source_match = re.search(
    r'bool AdvancePendingSources\(PendingEntryCommit pending,.*?\n        bool AdvancePendingPackedTerrain\(PendingEntryCommit pending,',
    renderer, re.S)
source = source_match.group(0) if source_match else ''
packed_match = re.search(
    r'bool AdvancePendingPackedTerrain\(PendingEntryCommit pending,.*?\n        Mesh UploadPreparedPackedTerrainMesh',
    renderer, re.S)
packed = packed_match.group(0) if packed_match else ''
check(bool(source), 'source preparation method resolved')
check(bool(packed), 'packed preparation method resolved')
check('int chunkItems = Rev35R005SourceChunkHardCap;' in source and
      'ResolveRev35R004PrepareChunkItems(budgetMilliseconds)' not in source,
      'source/geographic lane cannot widen above 64')
check('(iterations % chunkItems) == 0' in source and
      'mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >= budgetMilliseconds' in source,
      'source lane retains resumable measured-budget checkpoints')
check('int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);' in packed,
      'packed lane alone retains adaptive chunk resolver')
check('(iterations % chunkItems) == 0' in packed and
      'mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >= budgetMilliseconds' in packed,
      'packed lane retains measured-budget checkpoints')
check(renderer.count('int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);') == 1,
      'exactly one adaptive prepare lane remains')
check(renderer.count('operationHealthRev35R004AllocationContinues++;') == 3,
      'R004 budget-aware allocation continuation retained')
check('pending.PackedSource = new Vector3[count];' in packed and
      'pending.PackedColours = new Color32[count];' in packed and
      'pending.PackedIndices = new int[count];' in packed,
      'R002 split allocations retained')

for token in ('oh_rev35_r005_variant=',
              'oh_rev35_r005_source_chunk_cap=',
              'oh_rev35_r005_source_windows=',
              'oh_rev35_r005_packed_chunk_max_items='):
    check(token in renderer, 'telemetry output ' + token)
check('operationHealthRev35R005SourceHardCapWindows++' in source,
      'source hard-cap windows are observable')
check('operationHealthRev35R005PackedChunkMaxItems = Math.Max(' in packed,
      'packed adaptive width is observable')

check('REV3_5_R005_VARIANT="' + R005 + '"' in build,
      'build R005 identity present')
check('verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py' in build,
      'build invokes R005 verifier')
check('rev3_5_r005_variant=%s' in build,
      'candidate identity records R005')
check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz authority retained')
check('FinalizePendingEntryCommit(pending, system)' in renderer,
      'Finalize-only publication retained')
check('Rev35R003MaximumStaleSkipsPerWindow = 8' in renderer and
      'operationHealthRev35R003StaleCompletedSkips++' in renderer,
      'R003 requested-view anti-HOL admission retained')
check('YieldPendingEntryCommit(executedStage, stageStart, true)' in renderer,
      'REV003 three-argument yield contract retained')
check('pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' in renderer,
      'REV003 mesh authority successor retained')
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
