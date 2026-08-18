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


def ck(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


ck(R.is_file(), 'renderer exists')
ck(T.is_file(), 'tile system exists')
ck(B.is_file(), 'build exists')
if not R.is_file() or not T.is_file() or not B.is_file():
    raise SystemExit(1)
r = R.read_text(); t = T.read_text(); b = B.read_text()

# Parent/frozen identity.
ck(HF4 in r, 'HF4 parent retained')
ck(HF3 in t, 'HF3 complete-coverage parent retained')
ck(R007 in r, 'R007 marker present')
ck('Rev35R007FoundationQueueMaximum = 128' in r,
   'foundation queue hard cap exactly 128')
ck('Queue<string> rev35R007FoundationQueue' in r and
   'HashSet<string> rev35R007FoundationQueued' in r,
   'queue holds cache-key references with dedupe only')

# Resolve the exact inserted helpers by stable delimiters rather than a comment/string parser.
qs = r.find('void QueueRev35R007FoundationField(')
cs = r.find('bool TryBeginRev35R007QueuedFoundationCommit()', qs)
us = r.find('bool TryUploadRenderReadyField(', cs)
ck(qs >= 0 and cs > qs and us > cs, 'R007 helper regions resolved')
queue = r[qs:cs] if qs >= 0 and cs > qs else ''
chain = r[cs:us] if cs >= 0 and us > cs else ''

ck('tile.Key.Lod != AERISTerrainTileLod.Far' in queue,
   'only FAR foundation is chained')
ck('requested.Contains(cacheKey)' in queue and
   'entries.ContainsKey(cacheKey)' in queue,
   'queue requires latest request and missing Entry')
ck('rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum' in queue and
   'operationHealthRev35R007Overflow++' in queue,
   'queue is bounded and overflow observable')
ck('!contentSnapshotValid || !requested.Contains(cacheKey)' in chain,
   'chain revalidates latest content/request authority')
ck('entries.ContainsKey(cacheKey)' in chain and
   'renderReadyFields.TryGetValue(cacheKey' in chain,
   'chain skips committed/missing RenderReady fields')
ck('TryBeginPendingEntryCommit(field)' in chain and
   'operationHealthRev35R007ChainedBegins++' in chain,
   'chain enters existing single staged commit')
for token in ('new PendingEntryCommit', 'BuildEntry(', 'Task.Run(', 'new Thread(', 'ThreadPool.'):
    ck(token not in queue and token not in chain,
       'R007 helpers do not create independent commit/worker path: ' + token)

# TryUpload behavior: free lane starts immediately; occupied lane records exact FAR handoff.
ue = r.find('long ResolveRenderReadyLimitBytes()', us)
upload = r[us:ue] if us >= 0 and ue > us else ''
ck(upload, 'TryUploadRenderReadyField region resolved')
ck('if (pendingEntryCommit == null)' in upload and
   'TryBeginPendingEntryCommit(field)' in upload and
   'QueueRev35R007FoundationField(tile, cacheKey);' in upload,
   'RenderReady handoff removes 5 Hz re-admission gap')
ck('return true;' in upload,
   'duplicate raster suppression retained while field waits')

# Pump order and no-widening rails.
ps = r.find('void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
pe = r.find('bool TryBeginPendingEntryCommit(', ps)
pump = r[ps:pe] if ps >= 0 and pe > ps else ''
ck(pump, 'staged commit pump resolved')
chain_pos = pump.find('TryBeginRev35R007QueuedFoundationCommit();')
drain_pos = pump.find('rasterizer.Drain(completed, 1)')
ck(0 <= chain_pos < drain_pos,
   'queued RenderReady FAR admitted before raster FIFO')
ck('ResolveRev35R004CommitBudget(steadyCommitProfile)' in pump,
   'R004 adaptive budget still owns pump')
ck('publishedThisWindow < hardMaximum' in pump,
   'existing hardMaximum rail retained')
ck('Rev35R004BudgetMaximumMilliseconds = 2.00' in r,
   '2.00 ms commit ceiling unchanged')
ck('Rev35R005SourceChunkHardCap = 64' in r,
   'R005 source lane hard cap 64 unchanged')

# Draw ordering: invalidate old heading queue before pump, then rebuild queue from the fresh
# CaptureVisible/requested set. This is the turn-safety contract.
ds = r.find('internal AERISTerrainGpuDrawState Draw(Rect plot,')
de = r.find('void ResetFrontBufferState', ds)
draw = r[ds:de] if ds >= 0 and de > ds else ''
ck(draw, 'Draw region resolved')
a = draw.find('if (contentGeometryChanged)')
bp = draw.find('ResetRev35R007FoundationQueue();', a)
c = draw.find('PumpStagedCompletedCommit(system,', bp)
q = draw.find('requested.Clear();', c)
z = draw.find('ResetRev35R007FoundationQueue();', q)
ck(0 <= a < bp < c < q < z,
   'turn/range/view invalidation cannot chain previous requested viewport')

# Full-release hygiene.
rs = r.find('void ReleaseGpuResources()')
re = r.find('public void Dispose()', rs)
release = r[rs:re] if rs >= 0 and re > rs else ''
ck('ResetRev35R007FoundationQueue();' in release,
   'full GPU/viewport release clears queue references')

# Telemetry and install identity.
for token in (
    'oh_rev35_r007_variant=', 'oh_rev35_r007_queue=',
    'oh_rev35_r007_queue_peak=', 'oh_rev35_r007_queued=',
    'oh_rev35_r007_chain=', 'oh_rev35_r007_immediate=',
    'oh_rev35_r007_duplicate=', 'oh_rev35_r007_stale=',
    'oh_rev35_r007_already=', 'oh_rev35_r007_missing=',
    'oh_rev35_r007_overflow=', 'oh_rev35_r007_reset=',
):
    ck(token in r, 'telemetry ' + token)
ck('REV3_5_R007_VARIANT="' + R007 + '"' in b,
   'build R007 identity present')
ck('verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py' in b,
   'build invokes R007 verifier')
ck('rev3_5_r007_variant=%s' in b,
   'candidate identity records R007')

# Frozen visual/publication behavior.
ck('presentationNow + 0.10f' in r, 'fixed visible 10 Hz retained')
ck('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
   'ARGB32/Bilinear retained')
ck('foundationComplete = rendered && visible.FoundationComplete &&' in r and
   'lastBackFoundationCoverage >= 0.999f' in r and
   'readyFar >= visible.FarFoundationCount' in r,
   'strict FoundationComplete gate retained')
ck('FinalizePendingEntryCommit(pending, system)' in r,
   'Finalize-only publication retained')
ck('Rev35R003MaximumStaleSkipsPerWindow = 8' in r and
   'operationHealthRev35R003StalePendingCancels++' in r,
   'R003 stale authority retained')
ck('AcquireRev35R006Hf4ColourBuffer(count)' in r and
   'AcquireRev35R006Hf4IndexBuffer(count)' in r,
   'HF4 allocation recovery retained')
for token in (
    'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    ck(token not in r, 'rejected mechanism absent: ' + token)

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
