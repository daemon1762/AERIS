#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
D = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs'
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 COMPLETE COVERAGE CONTRACT HOTFIX3 VERIFY]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
HF2 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return text[start:i + 1]
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    return ''


for path, label in ((T, 'tile system'), (D, 'preload database'),
                    (R, 'renderer'), (B, 'build script')):
    check(path.is_file(), label + ' exists')
if not all(path.is_file() for path in (T, D, R, B)):
    raise SystemExit(1)

t = T.read_text(); d = D.read_text(); r = R.read_text(); b = B.read_text()
check(R006 in r, 'R006 managed-buffer parent retained')
check(HF1 in r, 'R006 resource-release HF1 retained')
check(HF2 in r, 'R006 resource-release-order HF2 retained')
check(HF3 in t, 'HF3 source-authority marker present')

complete = method(t,
    '        static bool IsCompleteCoverageAuthority(AERISTerrainHeightTile tile)')
replace = method(t,
    '        static bool CanReplaceCompleteCoverageAuthority(AERISTerrainHeightTile existing,')
reconcile = method(t,
    '        static bool ReconcileRequestWithRamTile(AERISTerrainTileRequest request,')
commit = method(t,
    '        void CommitFlightBlock(AERISTerrainTileRequest request,')

check(complete and 'tile.SamplingComplete' in complete and
      'tile.Quality < 100' in complete and
      'tile.Elevation.LongLength == expected' in complete and
      'tile.Flags.LongLength == expected' in complete,
      'complete authority requires SamplingComplete + Quality100 + full payload')
check(replace and 'incoming.Resolution > existing.Resolution' in replace and
      'incoming.Resolution < existing.Resolution' in replace and
      'existing.IsPreview && !incoming.IsPreview' in replace,
      'complete authority replacement is fidelity-monotonic')
check(reconcile and 'bool completeCoverage = IsCompleteCoverageAuthority(tile);' in reconcile and
      'if (completeCoverage && !tile.IsPreview' in reconcile and
      'if (completeCoverage &&' in reconcile,
      'RAM final/preview reconciliation uses complete-coverage authority')
check(commit and 'bool preserveCompleteAuthority' in commit and
      'IsCompleteCoverageAuthority(existing)' in commit and
      '!CanReplaceCompleteCoverageAuthority(existing, tile)' in commit,
      'CommitFlightBlock has complete-authority preservation gate')
check(commit and commit.find('if (preserveCompleteAuthority)') >= 0 and
      commit.find('if (preserveCompleteAuthority)') < commit.find('ram.Put(tile);'),
      'progressive regression is rejected before RAM authority replacement')
check(commit.count('ram.Put(tile);') == 1,
      'CommitFlightBlock has one guarded RAM publication point')
check('operationHealthRev35R006Hf3PreservedCompleteAuthority++' in commit and
      'operationHealthRev35R006Hf3IncompleteStageRetries++' in commit and
      'nextPlanRealtime = 0f;' in commit,
      'suppression and incomplete-stage retry telemetry are active')
check(commit.find('if (!requestComplete)') > commit.find('ram.Put(tile);'),
      'pipeline progressive commits continue only after guarded publication decision')
check(commit and 'ScheduleDiskWrite(tile.CloneImmutable());' in commit and
      commit.rfind('if (!IsCompleteCoverageAuthority(tile))') <
      commit.rfind('ScheduleDiskWrite(tile.CloneImmutable());'),
      'SSD write occurs only after final complete-coverage validation')
check('hf3_preserve=' in t and 'hf3_retry=' in t and 'hf3_worst_q=' in t,
      'CP3 telemetry publishes HF3 monotonic-authority witnesses')

contains = method(d, '        internal bool Contains(AERISTerrainTileKey key)')
chunk = method(d, '        internal bool TryGetChunkId(AERISTerrainTileKey key,')
snapshot = method(d,
    '        internal AERISTerrainTileKey[] SnapshotCompleteKeysForBody(string bodyName,')
publish = method(d, '        void PublishMapIndexLocked(string cause)')
savebatch = method(d, '        internal bool SaveBatch(')
if not savebatch:
    # Save(single tile) delegates into the batch implementation in current DB versions.
    marker = 'tile == null || tile.IsPreview || !tile.SamplingComplete ||'
    pos = d.find(marker)
    if pos >= 0:
        savebatch = d[max(0, pos - 800):pos + 900]

check(contains and 'entry.Quality >= 100' in contains,
      'fallback database presence requires Quality100')
check(chunk and 'entry.Quality < 100' in chunk,
      'fallback chunk lookup quarantines incomplete coverage')
check(snapshot and 'entry.Quality < 100' in snapshot,
      'resident population snapshot quarantines incomplete coverage')
check(savebatch and 'tile.Quality < 100' in savebatch,
      'preload persistence rejects Quality<100 tiles')
check(publish and 'entry.State != AERISTerrainGenerationState.Complete' in publish and
      'entry.Quality < 100' in publish,
      'Map DRAM publication exposes only Complete Quality100 metadata')
check('RemoveTileIndex(' not in commit,
      'HF3 Flight repair does not destructively mutate global DB index generation')

check('REV3_5_R006_HOTFIX3="' + HF3 + '"' in b,
      'build identity includes HF3')
check('verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py' in b,
      'build invokes HF3 verifier')
check('rev3_5_r006_hotfix3=%s' in b,
      'candidate identity records HF3')
check('presentationNow + 0.10f' in r,
      'fixed visible 10 Hz authority unchanged')
check('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
      'Golden ARGB32/Bilinear unchanged')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.',
                  'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
                  'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    check(forbidden not in r, 'rejected mechanism absent: ' + forbidden)

failed = [label for ok, label in checks if not ok]
if failed:
    print(PREFIX + ' FAIL: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
