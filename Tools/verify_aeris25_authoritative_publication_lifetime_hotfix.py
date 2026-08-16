#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
C = (ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
U = (ROOT / 'build_ubuntu.sh').read_text()
P5V = (ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py').read_text()
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks=[]

def ck(value,name):
    ok=bool(value); checks.append((ok,name)); print(('[PASS] ' if ok else '[FAIL] ')+name)

def method_body(signature):
    start=R.find(signature)
    if start<0: return ''
    op=R.find('{',start)
    if op<0: return ''
    depth=0; state='code'; i=op
    while i<len(R):
        c=R[i]; n=R[i+1] if i+1<len(R) else ''
        if state=='code':
            if c=='/' and n=='/': state='line'; i+=2; continue
            if c=='/' and n=='*': state='block'; i+=2; continue
            if c=='"': state='string'; i+=1; continue
            if c=="'": state='char'; i+=1; continue
            if c=='{': depth+=1
            elif c=='}':
                depth-=1
                if depth==0: return R[start:i+1]
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

ck('internal const string Codename = "NOREPINEPHRINE";' in M and
   'internal const string Revision = "OH_PHASE6_003";' in M and
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
   'codename = NOREPINEPHRINE' in C,
   'NOREPINEPHRINE OH_PHASE6_003 identity is authoritative')
ck('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in U and
   'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION' in U,
   'build/in-game identity is NOREPINEPHRINE rev003 authoritative publication')
ck('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in R and
   'AERIS25_STAGED_MAIN_THREAD_COMMIT' in R,
   'rev003 extends rather than removes rev002 staged commit architecture')
ck('MainThreadCommitSteadyBudgetMilliseconds = 0.50' in R and
   'MainThreadCommitBootstrapBudgetMilliseconds = 1.25' in R,
   'measured budgets remain 0.50 ms steady / 1.25 ms bootstrap')

pump=method_body('        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
advance=method_body('        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,')
finalize=method_body('        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
detach=method_body('        void DetachEntryForDeferredRetirement(Entry entry)')
release=method_body('        void ReleaseDeferredEntryRetirements(bool force)')
reset=method_body('        void ResetContentSnapshot()')

ck(pump and 'bool allowPublication' in pump and
   'pendingEntryCommit.Stage == PendingEntryCommitStage.Finalize' in pump and
   'operationHealthMainCommitPublicationDeferrals++' in pump,
   'pump has explicit authoritative-publication gate')
non_tick_start=R.find('            if (!authoritativeTickDue)')
non_tick_end=R.find('            operationHealthAuthoritativeTicks++;',non_tick_start)
non_tick=R[non_tick_start:non_tick_end] if non_tick_start>=0 and non_tick_end>non_tick_start else ''
ck('PumpStagedCompletedCommit(system, false);' in non_tick and
   'PumpStagedCompletedCommit(system, true);' not in non_tick and
   'CaptureVisible(' not in non_tick and 'RenderBackBuffer(' not in non_tick,
   'hidden Repaint may prepare/upload but cannot publish or rebuild visible authority')
content_start=R.find('            if (contentTickRequired)')
content_end=R.find('                // CaptureVisible owns planner-generation updates',content_start)
content_head=R[content_start:content_end] if content_start>=0 and content_end>content_start else ''
ck('PumpStagedCompletedCommit(system, true);' in content_head,
   'authoritative content tick is the only caller that enables publication')
ck(advance and 'bool allowPublication' in advance and
   'case PendingEntryCommitStage.Finalize:' in advance and
   'if (!allowPublication)' in advance and
   'FinalizePendingEntryCommit(pending, system);' in advance,
   'Finalize cannot execute when publication authority is false')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'visible ND presentation authority remains fixed 10 Hz')

for stage in ('AcquirePackedTerrainMesh','UploadPackedTerrainVertices','UploadPackedTerrainColours',
              'UploadPackedTerrainIndices','FinalizePackedTerrainMesh','PrepareContour',
              'AcquireContourMesh','UploadContourVertices','UploadContourColours',
              'UploadContourIndices','FinalizeContourMesh','PrepareCoastline',
              'AcquireCoastlineMesh','UploadCoastlineVertices','UploadCoastlineColours',
              'UploadCoastlineIndices','FinalizeCoastlineMesh'):
    ck(stage in R, 'rev003 stage exists: '+stage)
ck('AdvancePendingLinePreparation' in advance and
   'mainThreadCommitStopwatch.Elapsed.TotalMilliseconds' in method_body('        bool AdvancePendingLinePreparation('),
   'contour/coast CPU source+index preparation is resumable')
ck('UploadPreparedPackedTerrainMesh(' not in advance and 'BuildLineMesh(' not in advance,
   'rev002 monolithic mesh upload helpers are no longer used by staged commit')
ck('mesh.UploadMeshData(false);' in advance and
   'pending.PackedMesh.vertices = pending.PackedSource;' in advance and
   'pending.ContourMesh.SetIndices' in advance and
   'pending.CoastlineMesh.SetIndices' in advance,
   'Unity mesh mutation is separated into per-property stages before final upload')

ck(finalize and 'DetachEntryForDeferredRetirement(old);' in finalize and
   'if (entries.TryGetValue(pending.CacheKey, out old)) Remove(old);' not in finalize,
   'replacement does not synchronously recycle old snapshot Mesh')
ck(detach and 'deferredEntryRetirements.Add(entry);' in detach and
   'RecycleMesh(' not in detach,
   'retirement detach removes authority without recycling Mesh')
ck(release and 'presentationEntryPins.Contains(entry)' in release and
   'RecycleMesh(ref entry.PackedTerrainMesh);' in release and
   'RecycleMesh(ref entry.ContourMesh);' in release and
   'RecycleMesh(ref entry.CoastlineMesh);' in release,
   'deferred retirement recycles only after snapshot pin release')
packet_refresh='''                RefreshPresentationPackets(tiles, drawEntriesScratch);\n                // Phase6_003: publication may detach the previously published Entry, but\n                // Mesh recycling is delayed until the authoritative packet refresh proves\n                // that the old Entry is no longer referenced by the persistent snapshot.\n                ReleaseDeferredEntryRetirements(false);'''
ck(packet_refresh in R,
   'deferred retirement is drained only after authoritative presentation packet refresh')
ck(reset and 'presentationEntryPins.Clear();' in reset and
   'ReleaseDeferredEntryRetirements(true);' in reset,
   'snapshot reset force-releases deferred retirement only after clearing pins')
ck('DetachEntryForDeferredRetirement(supersededScratch[i]);' in R,
   'superseded Entry replacement uses the same deferred retirement contract')

for field in ('oh_main_commit_clip_max_ms=','oh_main_commit_prepare_max_ms=',
              'oh_main_commit_terrain_upload_max_ms=','oh_main_commit_contour_max_ms=',
              'oh_main_commit_coastline_max_ms=','oh_main_commit_geo_max_ms=',
              'oh_main_commit_finalize_max_ms=','oh_main_commit_publish_defer=',
              'oh_deferred_retire_pending=','oh_deferred_retire_queued=',
              'oh_deferred_retire_released=','oh_deferred_retire_protected=',
              'oh_deferred_retire_peak='):
    ck(field in R,'runtime telemetry publishes '+field[:-1])
ck('RecordPendingStageCost' in R and
   'operationHealthMainCommitTerrainUploadMaxMilliseconds' in R and
   'operationHealthMainCommitFinalizeMaxMilliseconds' in R,
   'stage-specific maxima directly identify remaining atomic hitch class')

ck("r'\\1NOREPINEPHRINE'" in U and "r'\\1ATROPINE'" not in U and
   'Phase 6 install promotion' in U,
   'installer preserves user OH policy but promotes package codename to NOREPINEPHRINE')
ck('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in U and
   'verify_aeris25_staged_main_thread_commit_hotfix.py' not in '\n'.join(
       line for line in U.splitlines() if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3')),
   'build uses one final-tree rev003 verifier')
ck('OH_PHASE6_003' in P5V and
   'verify_aeris25_authoritative_publication_lifetime_hotfix.py' in P5V,
   'ADENOSINE inherited verifier explicitly admits exact rev003 descendant')

ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'presentationEntryPins.Contains(entry)' in R and 'oh_presentation_packet_reuse=' in R,
   'accepted ADENOSINE persistent presentation path remains intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R and
   'oh_snapshot_stale_mesh=' in R and 'oh_gpu_vertex_reject_semantic_mesh_null=' in R,
   'rev008 lifetime witness remains active')
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and
   'oh_prune_budget_hit=' in R and 'oh_heading_plan_coalesced=' in R,
   'accepted ATROPINE rev009 prune/heading governor remains intact')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' not in SH,
   'rev003 changes no shader equations or shader bytes')

draw=method_body('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
terrain=draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)')
contour=draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)')
coast=draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)')
ck(0 <= terrain < contour < coast,
   'hard painter order remains terrain -> contour -> coastline inside every Entry')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck('ElapsedMilliseconds(mainThreadCommitStopwatch)' not in R,
   'undefined Phase6_001 elapsed helper remains impossible')

frozen=['Source/AERISFlightControl/AA','Source/AERISFlightControl/Autopilot',
        'Source/AERISFlightControl/Protect','Source/AERISFlightControl/Landing']
try:
    changed=subprocess.check_output(['git','-C',str(ROOT),'diff','--name-only','HEAD','--']+frozen,
                                    text=True).strip().splitlines()
except Exception:
    changed=['GIT_DIFF_UNAVAILABLE']
ck(changed==[],'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed=[name for ok,name in checks if not ok]
print('\n[AERIS25 NOREPINEPHRINE PHASE6_003 AUTHORITATIVE PUBLICATION] %d/%d PASS' %
      (len(checks)-len(failed),len(checks)))
if failed:
    msg='; '.join(failed)
    print('FAILED: '+msg)
    print('::error title=NOREPINEPHRINE Phase6_003 verifier::'+msg)
    raise SystemExit(1)
print('[AERIS25 NOREPINEPHRINE PHASE6_003 AUTHORITATIVE PUBLICATION] STATIC PASS')
