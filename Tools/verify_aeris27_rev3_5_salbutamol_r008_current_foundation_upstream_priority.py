#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
Z = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
S = ROOT / 'Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 R008 CURRENT FOUNDATION UPSTREAM PRIORITY VERIFY]'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'
R008 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
checks=[]


def check(value, label):
    ok=bool(value); checks.append((ok,label)); print(('[PASS] ' if ok else '[FAIL] ')+label)


def method_body(text, signature):
    start=text.find(signature)
    if start < 0: return ''
    op=text.find('{',start)
    if op < 0: return ''
    depth=0; state='code'; i=op
    while i < len(text):
        c=text[i]; n=text[i+1] if i+1 < len(text) else ''
        if state=='code':
            if c=='/' and n=='/': state='line'; i+=2; continue
            if c=='/' and n=='*': state='block'; i+=2; continue
            if c=='"': state='string'; i+=1; continue
            if c=="'": state='char'; i+=1; continue
            if c=='{': depth+=1
            elif c=='}':
                depth-=1
                if depth==0: return text[start:i+1]
            i+=1; continue
        if state=='line':
            if c=='\n': state='code'
            i+=1; continue
        if state=='block':
            if c=='*' and n=='/': state='code'; i+=2; continue
            i+=1; continue
        if state=='string':
            if c=='\\': i+=2; continue
            if c=='"': state='code'
            i+=1; continue
        if state=='char':
            if c=='\\': i+=2; continue
            if c=="'": state='code'
            i+=1; continue
    return ''


for p,label in ((R,'renderer'),(Z,'rasterizer'),(S,'scheduler'),(T,'tile system'),(B,'build')):
    check(p.is_file(), label+' exists')
if not all(p.is_file() for p in (R,Z,S,T,B)): raise SystemExit(1)
r=R.read_text(); z=Z.read_text(); s=S.read_text(); t=T.read_text(); b=B.read_text()
check(R007 in r, 'R007 parent retained')
check(HF4 in r, 'HF4 parent retained')
check(HF3 in t, 'HF3 parent retained')
check(R008 in r and R008 in z, 'R008 renderer+rasterizer marker present')

# Rasterizer identity and stale-work retirement.
check(z.count('internal string RequestIdentity;') >= 2,
      'request/result carry exact RequestIdentity')
check('internal string RequestIdentity;' in method_body(z, '        sealed class PendingState'),
      'pending state carries exact RequestIdentity')
enqueue=method_body(z,'        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)')
check(enqueue and 'string.IsNullOrEmpty(request.RequestIdentity)' in enqueue,
      'raster enqueue fails closed without exact identity')
check('RequestIdentity = request.RequestIdentity' in enqueue,
      'pending capture records request identity')
reconcile=method_body(z,'        internal void ReconcileCurrentRequests(HashSet<string> currentRequestIdentities)')
check(reconcile, 'raster current-request reconcile method resolved')
check('!currentRequestIdentities.Contains(state.RequestIdentity)' in reconcile and
      'pending.Remove(rev35R008CancelTileIdsScratch[i])' in reconcile,
      'obsolete pending raster requests retire before new admission')
check('runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute' in reconcile,
      'obsolete queued/active terrain jobs invalidate through existing scheduler CancelKey')
check('currentRequestIdentities.Contains(result.RequestIdentity)' in reconcile and
      'rev35R008CompletedDropped++' in reconcile,
      'obsolete local completed results are dropped before renderer admission')
check('rev35R008CompletedScratch' in reconcile and
      'while (rev35R008CompletedScratch.Count > 0)' in reconcile,
      'current completed results preserve bounded FIFO order')
check('Queue<AERISTerrainGpuTileRasterResult>(64)' in z,
      'R008 completed reconcile scratch is bounded to existing 64-result ceiling')
check('RequestIdentity = request.RequestIdentity' in z and
      'StyleKey = request.StyleKey, RequestIdentity = request.RequestIdentity' in z,
      'worker result preserves exact request identity')

# Renderer must establish all exact current keys before scheduling any new worker job.
draw=method_body(r,'        internal AERISTerrainGpuDrawState Draw(Rect plot,')
check(draw, 'Draw method resolved')
prepass=draw.find('R008 phase 1: establish the complete current exact request set first')
reconcile_call=draw.find('rasterizer.ReconcileCurrentRequests(requested);')
admission=draw.find('for (int admissionPass = 0; admissionPass < 2; admissionPass++)')
check(0 <= prepass < reconcile_call < admission,
      'complete requested set -> raster reconcile -> new admission ordering is strict')
check('requested.Add(CacheKey(requestedTile.Key,' in draw,
      'all visible exact identities are established in prepass')
check('bool r008Foundation = tile.Key.Lod == AERISTerrainTileLod.Far;' in draw and
      'if ((admissionPass == 0) != r008Foundation) continue;' in draw,
      'FAR foundation scheduling is first pass; other LODs second pass')
check('!requested.Contains(pendingEntryCommit.CacheKey)' in draw and
      'CancelPendingEntryCommit();' in draw and
      'operationHealthRev35R008PendingCommitCancelled++' in draw,
      'existing single main-thread pending commit is reconciled after exact request set exists')

pump=method_body(r,'        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
check(pump and 'if (rev35R008GeometryReconcilePending)' in pump and
      'operationHealthRev35R008GeometryPumpSuppress++' in pump and 'return;' in pump,
      'geometry change cannot advance old-view pump before new request reconciliation')
check('ResolveRev35R004CommitBudget(steadyCommitProfile)' in pump and
      'publishedThisWindow < hardMaximum' in pump,
      'R004 adaptive budget and publication hard rail retained')

schedule=method_body(r,'        void Schedule(AERISTerrainHeightTile tile, string cacheKey, string styleKey,')
check(schedule and 'RequestIdentity = cacheKey' in schedule,
      'renderer schedule passes exact cache identity to rasterizer')
check('tile.Key.Lod == AERISTerrainTileLod.Far' in schedule and
      'operationHealthRev35R008FoundationScheduleFirst++' in schedule,
      'FAR worker admissions are observable')

# R007 remains useful for fields that are already RenderReady.
check('TryBeginRev35R007QueuedFoundationCommit();' in pump and
      'Rev35R007FoundationQueueMaximum = 128' in r,
      'R007 downstream chain remains intact')
check('AcquireRev35R006Hf4ColourBuffer(count)' in r and
      'AcquireRev35R006Hf4IndexBuffer(count)' in r,
      'HF4 packed allocation recovery retained')
check('hf3_preserve=' in t and 'hf3_retry=' in t,
      'HF3 complete-coverage contract retained')

# R008 does not alter the shared scheduler implementation or introduce worker mechanics.
check('SubmitLatest(AERISRuntimeLane lane, string key,' in s and
      'Job TakeNextLocked()' in s,
      'existing shared scheduler API/fairness implementation remains present')
for forbidden in ('SubmitLatestPriority', 'SubmitFront', 'FoundationPriority',
                  'Task.Run(', 'WaitManagedPreparation', 'ResidentPreparedPresentation',
                  'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
                  'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in r and forbidden not in z,
          'R008 runtime excludes rejected/new scheduler mechanism: '+forbidden)

# Frozen visual/publication contract.
check('presentationNow + 0.10f' in r, 'fixed visible 10 Hz retained')
check('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
      'Golden ARGB32/Bilinear retained')
check('foundationComplete = rendered && visible.FoundationComplete &&' in r and
      'lastBackFoundationCoverage >= 0.999f' in r and
      'readyFar >= visible.FarFoundationCount' in r,
      'strict FoundationComplete swap gate retained')
check('FinalizePendingEntryCommit(pending, system)' in r,
      'Finalize-only publication retained')
check('Rev35R003MaximumStaleSkipsPerWindow = 8' in r,
      'R003 renderer stale-admission rail retained')
check('Rev35R005SourceChunkHardCap = 64' in r,
      'R005 Source 64 hard cap retained')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in r,
      'R004 2 ms commit ceiling retained')

for token in (
    'oh_rev35_r008_variant=', 'oh_rev35_r008_pump_suppress=',
    'oh_rev35_r008_pending_cancel=', 'oh_rev35_r008_far_schedule=',
    'oh_rev35_r008_reconcile=', 'oh_rev35_r008_raster_pending_cancel=',
    'oh_rev35_r008_raster_completed_drop=', 'oh_rev35_r008_scheduler_cancel=',
):
    check(token in r, 'runtime telemetry publishes '+token)
check('REV3_5_R008_VARIANT="'+R008+'"' in b,
      'build records R008 identity')
check('verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py' in b,
      'build invokes R008 verifier')
check('rev3_5_r008_variant=%s' in b,
      'candidate identity records R008')

failed=[label for ok,label in checks if not ok]
print('\n'+PREFIX+' %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+'; '.join(failed)); raise SystemExit(1)
print(PREFIX+' STATIC PASS')
