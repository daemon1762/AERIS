#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R002 VERIFY]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
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
check(R001 in renderer, 'R001 parent retained')
check(R002 in renderer, 'R002 marker present')
check('YieldPendingEntryCommit(executedStage, stageStart, true)' in renderer,
      'REV003 three-argument yield contract retained')
check('pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' in renderer,
      'REV003 AcquirePackedTerrainMesh successor retained')

start = renderer.find('bool AdvancePendingPackedTerrain(PendingEntryCommit pending,')
end = renderer.find('Mesh UploadPreparedPackedTerrainMesh', start)
method = renderer[start:end] if start >= 0 and end > start else ''
check(bool(method), 'packed prepare method resolved')
for case, alloc in ((1, 'pending.PackedSource = new Vector3[count];'),
                    (2, 'pending.PackedColours = new Color32[count];'),
                    (3, 'pending.PackedIndices = new int[count];')):
    pattern = re.compile(r'case %d:.*?%s.*?operationHealthRev35PreparePackedYields\+\+;.*?return false;' %
                         (case, re.escape(alloc)), re.S)
    check(bool(pattern.search(method)), 'allocation case %d is isolated and forced-yield' % case)
check(method.count('pending.PackedSource = new Vector3[count];') == 1,
      'exactly one packed source allocation')
check(method.count('pending.PackedColours = new Color32[count];') == 1,
      'exactly one packed colour allocation')
check(method.count('pending.PackedIndices = new int[count];') == 1,
      'exactly one packed index allocation')
check('pending.PackedSource = new Vector3[vertexCount];' not in method and
      'pending.PackedColours = new Color32[vertexCount];' not in method and
      'pending.PackedIndices = new int[indexCount];' not in method,
      'R001 three-allocation atomic block removed')
check('case 11:' in method and 'pending.PrepareSubstage = 12;' in method,
      'shifted R001 copy/index state machine reaches final state 12 from case 11')
for name in ('operationHealthRev35PackedSourceAllocMaxMs',
             'operationHealthRev35PackedColourAllocMaxMs',
             'operationHealthRev35PackedIndexAllocMaxMs'):
    check(name in renderer, 'telemetry field ' + name)
for token in ('oh_rev35_packed_source_alloc_max_ms=',
              'oh_rev35_packed_colour_alloc_max_ms=',
              'oh_rev35_packed_index_alloc_max_ms='):
    check(token in renderer, 'telemetry output ' + token)
check('REV3_5_R002_VARIANT="' + R002 + '"' in build,
      'build R002 identity present')
check('verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py' in build,
      'build invokes R002 verifier')
check('rev3_5_r002_variant=%s' in build,
      'candidate identity records R002')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
                  'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in method, 'R002 packed region excludes ' + forbidden)
check('presentationNow + 0.10f' in renderer, 'fixed visible 10 Hz witness retained')
check('FinalizePendingEntryCommit(pending, system)' in renderer,
      'Finalize-only publication retained')
failed = [label for ok, label in checks if not ok]
if failed:
    print(PREFIX + ' FAIL count=%d' % len(failed))
    raise SystemExit(1)
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
