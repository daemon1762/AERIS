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
hf4 = HF4 in renderer
for marker in (R001, R002, R003, R004, R005):
    check(marker in renderer, 'lineage marker retained: ' + marker)
check('const int Rev35R005SourceChunkHardCap = 64;' in renderer,
      'source/geographic heavy lane hard cap is exactly 64')
check('Rev35R004PrepareChunkMedium = 128' in renderer and
      'Rev35R004PrepareChunkHigh = 256' in renderer,
      'packed lane retains R004 64/128/256 capacity')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in renderer and
      'ResolveRev35R004CommitBudget(steadyCommitProfile)' in renderer,
      'R004 adaptive 0.50..2.00 ms budget retained')
source_match = re.search(
    r'bool AdvancePendingSources\(PendingEntryCommit pending,.*?\n        bool AdvancePendingPackedTerrain\(PendingEntryCommit pending,',
    renderer, re.S)
packed_match = re.search(
    r'bool AdvancePendingPackedTerrain\(PendingEntryCommit pending,.*?\n        Mesh UploadPreparedPackedTerrainMesh',
    renderer, re.S)
source = source_match.group(0) if source_match else ''
packed = packed_match.group(0) if packed_match else ''
check(bool(source) and bool(packed), 'source and packed methods resolved')
check('int chunkItems = Rev35R005SourceChunkHardCap;' in source and
      'ResolveRev35R004PrepareChunkItems(budgetMilliseconds)' not in source,
      'source lane cannot widen above 64')
check('(iterations % chunkItems) == 0' in source and
      'mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=' in source,
      'source lane remains resumable and budget checked')
check('int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);' in packed and
      '(iterations % chunkItems) == 0' in packed,
      'packed lane alone retains adaptive chunk resolver')
check(renderer.count('int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);') == 1,
      'exactly one adaptive prepare lane remains')
check(renderer.count('operationHealthRev35R004AllocationContinues++;') == 3,
      'R004 budget-aware allocation/acquire continuation retained')
if hf4:
    check('pending.PackedSource = new Vector3[count];' in packed and
          'AcquireRev35R006Hf4ColourBuffer(count)' in packed and
          'AcquireRev35R006Hf4IndexBuffer(count)' in packed and
          'pending.PackedColours = new Color32[count];' not in packed and
          'pending.PackedIndices = new int[count];' not in packed,
          'HF4 keeps source allocation and bounded colour/index acquire')
else:
    check('pending.PackedSource = new Vector3[count];' in packed and
          'pending.PackedColours = new Color32[count];' in packed and
          'pending.PackedIndices = new int[count];' in packed,
          'R002 split allocations retained')
for token in ('oh_rev35_r005_variant=', 'oh_rev35_r005_source_chunk_cap=',
              'oh_rev35_r005_source_windows=', 'oh_rev35_r005_packed_chunk_max_items='):
    check(token in renderer, 'telemetry output ' + token)
check('operationHealthRev35R005SourceHardCapWindows++' in source,
      'source hard-cap windows observable')
check('operationHealthRev35R005PackedChunkMaxItems = Math.Max(' in packed,
      'packed adaptive width observable')
check('REV3_5_R005_VARIANT="' + R005 + '"' in build and
      'verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py' in build and
      'rev3_5_r005_variant=%s' in build,
      'R005 build and candidate identity retained')
check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz authority retained')
check('FinalizePendingEntryCommit(pending, system)' in renderer,
      'Finalize-only publication retained')
check('Rev35R003MaximumStaleSkipsPerWindow = 8' in renderer and
      'operationHealthRev35R003StaleCompletedSkips++' in renderer,
      'R003 anti-HOL admission retained')
check('YieldPendingEntryCommit(executedStage, stageStart, true)' in renderer and
      'pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' in renderer,
      'REV003 staged commit authority retained')
failed = [label for ok, label in checks if not ok]
if failed:
    print(PREFIX + ' FAIL count=%d' % len(failed))
    raise SystemExit(1)
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
