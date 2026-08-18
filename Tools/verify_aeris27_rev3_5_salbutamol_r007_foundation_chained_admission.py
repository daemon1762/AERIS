#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 R007 FOUNDATION CHAINED ADMISSION VERIFY]'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method_body(text, signature):
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


check(R.is_file(), 'renderer exists')
check(T.is_file(), 'tile system exists')
check(B.is_file(), 'build exists')
if not R.is_file() or not T.is_file() or not B.is_file():
    raise SystemExit(1)
r = R.read_text(); t = T.read_text(); b = B.read_text()
check(HF4 in r, 'HF4 packed managed-buffer parent retained')
check(HF3 in t, 'HF3 complete-coverage parent retained')
check(R007 in r, 'R007 marker present')
check('Rev35R007FoundationQueueMaximum = 128' in r,
      'foundation handoff queue hard cap exactly 128')
check('Queue<string> rev35R007FoundationQueue' in r and
      'HashSet<string> rev35R007FoundationQueued' in r,
      'R007 stores only bounded cache-key references plus dedupe set')
check('Queue<AERISTerrainGpuTileRasterResult>' not in method_body(
          r, '        void QueueRev35R007FoundationField('),
      'R007 queue helper does not retain raster result queues')

queue = method_body(r, '        void QueueRev35R007FoundationField(')
chain = method_body(r, '        bool TryBeginRev35R007QueuedFoundationCommit()')
check(queue, 'R007 queue helper resolved')
check(chain, 'R007 chain helper resolved')
check('tile.Key.Lod != AERISTerrainTileLod.Far' in queue,
      'only FAR foundation fields enter chained queue')
check('requested.Contains(cacheKey)' in queue and
      'entries.ContainsKey(cacheKey)' in queue,
      'queue admission is current-request and not-already-committed gated')
check('rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum' in queue and
      'operationHealthRev35R007Overflow++' in queue,
      'queue overflow fails back to legacy retry path and is observable')
check('!contentSnapshotValid || !requested.Contains(cacheKey)' in chain,
      'chained admission revalidates latest requested viewport')
check('entries.ContainsKey(cacheKey)' in chain and
      'renderReadyFields.TryGetValue(cacheKey' in chain,
      'chained admission requires missing Entry but existing RenderReady field')
check('TryBeginPendingEntryCommit(field)' in chain and
      'operationHealthRev35R007ChainedBegins++' in chain,
      'chain begins through existing single staged-commit authority')
for forbidden in ('new PendingEntryCommit', 'BuildEntry(', 'Mesh', 'Task.Run(',
                  'new Thread(', 'ThreadPool.'):
    check(forbidden not in queue and forbidden not in chain,
          'queue/chain helper excludes independent commit or worker mechanism: ' + forbidden)

upload = method_body(r,
    '        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string cacheKey,')
check(upload, 'TryUploadRenderReadyField resolved')
check('if (pendingEntryCommit == null)' in upload and
      'TryBeginPendingEntryCommit(field)' in upload and
      'QueueRev35R007FoundationField(tile, cacheKey);' in upload,
      'free lane starts immediately; occupied lane queues current FAR handoff')
check('return true;' in upload,
      'existing duplicate-raster suppression remains')

pump = method_body(r,
    '        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
check(pump, 'staged commit pump resolved')
chain_pos = pump.find('TryBeginRev35R007QueuedFoundationCommit();')
drain_pos = pump.find('rasterizer.Drain(completed, 1)')
check(0 <= chain_pos < drain_pos,
      'already RenderReady current foundation is admitted before raster FIFO')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in r,
      'R004 2.00 ms maximum commit budget unchanged')
check('ResolveRev35R004CommitBudget(steadyCommitProfile)' in pump,
      'existing adaptive budget resolver still owns pump budget')
check('publishedThisWindow < hardMaximum' in pump,
      'existing hardMaximum publication rail retained')
check('pendingEntryCommit == null' in pump,
      'single pending staged-commit slot retained')

# Geometry changes must not consume old queued keys before the new exact request set exists.
draw = method_body(r,
    '        internal AERISTerrainGpuDrawState Draw(Rect plot,')
check(draw, 'Draw resolved')
geometry_reset = draw.find('if (contentGeometryChanged)\n                    ResetRev35R007FoundationQueue();')
pump_call = draw.find('PumpStagedCompletedCommit(system,')
requested_clear = draw.find('requested.Clear();')
fresh_reset = draw.find('ResetRev35R007FoundationQueue();', requested_clear)
check(0 <= geometry_reset < pump_call,
      'geometry invalidation clears previous-view chain before pump')
check(0 <= requested_clear < fresh_reset,
      'fresh CaptureVisible request set rebuild resets and deterministically rebuilds queue')

release = method_body(r, '        void ReleaseGpuResources()')
check(release and 'ResetRev35R007FoundationQueue();' in release,
      'full GPU/viewport teardown clears queue references')

for token in (
    'oh_rev35_r007_variant=', 'oh_rev35_r007_queue=',
    'oh_rev35_r007_queue_peak=', 'oh_rev35_r007_queued=',
    'oh_rev35_r007_chain=', 'oh_rev35_r007_immediate=',
    'oh_rev35_r007_duplicate=', 'oh_rev35_r007_stale=',
    'oh_rev35_r007_already=', 'oh_rev35_r007_missing=',
    'oh_rev35_r007_overflow=', 'oh_rev35_r007_reset=',
):
    check(token in r, 'runtime telemetry publishes ' + token)

check('REV3_5_R007_VARIANT="' + R007 + '"' in b,
      'build records R007 identity')
check('verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py' in b,
      'build invokes R007 verifier')
check('rev3_5_r007_variant=%s' in b,
      'candidate identity records R007')

# Frozen contracts.
check('presentationNow + 0.10f' in r, 'fixed visible 10 Hz retained')
check('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
      'Golden ARGB32/Bilinear retained')
check('foundationComplete = rendered && visible.FoundationComplete &&' in r and
      'lastBackFoundationCoverage >= 0.999f' in r and
      'readyFar >= visible.FarFoundationCount' in r,
      'FoundationComplete swap gate remains strict')
check('FinalizePendingEntryCommit(pending, system)' in r,
      'Finalize-only publication path retained')
check('Rev35R003MaximumStaleSkipsPerWindow = 8' in r and
      'operationHealthRev35R003StalePendingCancels++' in r,
      'R003 requested-view anti-HOL authority retained')
check('Rev35R005SourceChunkHardCap = 64' in r,
      'R005 source lane hard cap 64 retained')
check('AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4' in r and
      'AcquireRev35R006Hf4ColourBuffer(count)' in r,
      'HF4 allocation recovery retained')
for forbidden in (
    'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    check(forbidden not in r, 'rejected mechanism absent: ' + forbidden)

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
