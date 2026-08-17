#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
OH = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
B = ROOT / 'build_ubuntu.sh'
MARKER = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001 VERIFY]'

checks = []

def check(ok, label):
    ok = bool(ok)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)

for path in (R, OH, B):
    check(path.is_file(), 'exists ' + str(path.relative_to(ROOT)))
if not all(path.is_file() for path in (R, OH, B)):
    raise SystemExit(1)

renderer = R.read_text()
oh = OH.read_text()
build = B.read_text()

check('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in renderer,
      'REV003 authoritative publication parent retained')
check('AERIS25_STAGED_MAIN_THREAD_COMMIT' in renderer,
      'REV002 staged main-thread parent retained')
check(MARKER in renderer, 'REV3.5 runtime marker present')
check('const int Rev35PrepareChunkItems = 64;' in renderer,
      'bounded managed prepare chunk size=64')
check('bool AdvancePendingSources(PendingEntryCommit pending,' in renderer,
      'source preparation resumable method present')
check('bool AdvancePendingPackedTerrain(PendingEntryCommit pending,' in renderer,
      'packed preparation resumable method present')
check('void PreparePendingSources(PendingEntryCommit pending)' not in renderer,
      'atomic PreparePendingSources removed')
check('void PreparePendingPackedTerrain(PendingEntryCommit pending)' not in renderer,
      'atomic PreparePendingPackedTerrain removed')
check('if (!AdvancePendingSources(pending, budgetMilliseconds))' in renderer,
      'PrepareSources stage obeys resumable budget')
check('if (!AdvancePendingPackedTerrain(pending, budgetMilliseconds))' in renderer,
      'PreparePackedTerrain stage obeys resumable budget')
check('operationHealthRev35PrepareSourceYields++' in renderer and
      'operationHealthRev35PreparePackedYields++' in renderer,
      'prepare yield telemetry present')
check('oh_rev35_prepare_source_yield=' in renderer and
      'oh_rev35_prepare_packed_yield=' in renderer,
      'prepare yield summary fields present')

check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz authority witness retained')
check('PendingEntryCommitStage.Finalize' in renderer and
      'FinalizePendingEntryCommit(pending, system)' in renderer,
      'publish remains Finalize-only')
check('BuildLineMesh(' in renderer and 'AERIS_TERRAIN_CONTOUR_' in renderer and
      'AERIS_TERRAIN_COAST_' in renderer,
      'contour/coastline line presentation retained')
check('internal const string Codename = "NOREPINEPHRINE";' in oh and
      'internal const string Revision = "OH_PHASE6_003";' in oh and
      'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in oh,
      'REV003 behavior identity retained beneath REV3.5 overlay')
check('internal const string ObserverVariant = "AERIS26_REV003_OBSERVER_M1";' in oh,
      'REV003 Observer M1 retained')

for forbidden in (
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
    'WaitManagedPreparation',
    'ResidentPreparedPresentation',
):
    check(forbidden not in renderer, 'rejected mechanism absent: ' + forbidden)

start = renderer.find('const string Rev35Variant = "' + MARKER + '";')
end = renderer.find('bool FinalizePendingEntryCommit(', start)
rev35_region = renderer[start:end] if start >= 0 and end > start else ''
check(len(rev35_region) > 0, 'REV3.5 implementation region resolved')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation'):
    check(forbidden not in rev35_region,
          'REV3.5 adds no worker/thread escape: ' + forbidden)

check('REV3_5_VARIANT="' + MARKER + '"' in build,
      'build REV3.5 identity present')
check('verify_aeris27_rev3_5_salbutamol_resumable_prepare.py' in build,
      'build invokes REV3.5 verifier')
check('rev3_5_variant=%s' in build,
      'candidate identity records REV3.5 variant')

def brace_sane(text):
    depth = 0
    minimum = 0
    state = 'code'
    i = 0
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'str'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                minimum = min(minimum, depth)
            i += 1
            continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1
            continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1
            continue
        if state in ('str', 'char'):
            if c == '\\': i += 2; continue
            if (state == 'str' and c == '"') or (state == 'char' and c == "'"):
                state = 'code'
            i += 1
    return depth == 0 and minimum == 0 and state in ('code', 'line')

check(brace_sane(renderer), 'renderer C# brace sanity')

packed_match = re.search(
    r'bool AdvancePendingPackedTerrain\(PendingEntryCommit pending,.*?\n        Mesh UploadPreparedPackedTerrainMesh',
    renderer, re.S)
packed_text = packed_match.group(0) if packed_match else ''
check('Array.Copy(' not in packed_text,
      'managed packed preparation no longer uses atomic Array.Copy')

if R004 in renderer:
    adaptive_checkpoints = (
        'int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);' in packed_text and
        packed_text.count('(iterations % chunkItems) == 0') >= 2 and
        'mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=' in packed_text and
        'budgetMilliseconds' in packed_text)
    check(adaptive_checkpoints,
          'managed packed preparation has repeated adaptive budget checkpoints')
else:
    check('Rev35PrepareChunkItems' in packed_text,
          'managed packed preparation has repeated budget checkpoints')

failed = [label for ok, label in checks if not ok]
if failed:
    print(PREFIX + ' STATIC FAIL count=%d' % len(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS %d/%d' % (len(checks), len(checks)))
