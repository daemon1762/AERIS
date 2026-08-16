#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M=(ROOT/'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
C=(ROOT/'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
U=(ROOT/'build_ubuntu.sh').read_text()
P5V=(ROOT/'Tools/verify_aeris25_persistent_presentation_batching.py').read_text()
SH=(ROOT/'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks=[]
def ck(v,n):
    ok=bool(v); checks.append((ok,n)); print(('[PASS] ' if ok else '[FAIL] ')+n)

def method_body(signature):
    start=R.find(signature)
    if start<0:return ''
    op=R.find('{',start)
    if op<0:return ''
    depth=0;state='code';i=op
    while i<len(R):
        c=R[i];n=R[i+1] if i+1<len(R) else ''
        if state=='code':
            if c=='/' and n=='/':state='line';i+=2;continue
            if c=='/' and n=='*':state='block';i+=2;continue
            if c=='"':state='str';i+=1;continue
            if c=="'":state='char';i+=1;continue
            if c=='{':depth+=1
            elif c=='}':
                depth-=1
                if depth==0:return R[start:i+1]
            i+=1;continue
        if state=='line':
            if c=='\n':state='code'
            i+=1;continue
        if state=='block':
            if c=='*' and n=='/':state='code';i+=2;continue
            i+=1;continue
        if state in ('str','char'):
            if c=='\\':i+=2;continue
            if (state=='str' and c=='"') or (state=='char' and c=="'"):state='code'
            i+=1
    return ''

ck('internal const string Codename = "NOREPINEPHRINE";' in M and
   'internal const string Revision = "OH_PHASE6_004";' in M and
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
   'codename = NOREPINEPHRINE' in C,
   'NOREPINEPHRINE OH_PHASE6_004 identity is authoritative')
ck('REV004 MANAGED PREPARATION PIPELINE' in U and
   'verify_aeris25_managed_preparation_pipeline_hotfix.py' in U,
   'build/in-game identity and final verifier are rev004 managed preparation')
ck('AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in R and
   'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in R,
   'rev004 extends the accepted rev003 publication/lifetime architecture')

advance=method_body('        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,')
submit=method_body('        bool SubmitManagedPreparation(PendingEntryCommit pending)')
buildprep=method_body('        static ManagedPreparationPayload BuildManagedPreparation(')
applyprep=method_body('        void ApplyManagedPreparation(PendingEntryCommit pending)')
finalize=method_body('        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
cancel=method_body('        void CancelPendingEntryCommit()')
ensuregpu=method_body('        bool EnsureGpuVertexProjectionAttributes(Entry entry)')
fallback=method_body('        bool EnsureCpuProjectionFallbackData(Entry entry)')
project=method_body('        Matrix4x4 EnsureProjectedGeometry(Entry entry,')
release=method_body('        void ReleaseDeferredEntryRetirements(bool force)')

ck('SubmitManagedPreparation' in R and 'WaitManagedPreparation' in R and
   'sealed class ManagedPreparationPayload' in R,
   'explicit worker managed-preparation stages and payload exist')
ck(submit and 'runtime.Scheduler.SubmitRequired(' in submit and
   'AERISRuntimeLane.GeneralCompute' in submit and 'context.ThrowIfStale();' in submit and
   'Task.Run' not in submit and 'new Thread' not in submit,
   'managed preparation uses bounded existing AERIS GeneralCompute scheduler, never ad-hoc threads')
for forbidden in ('Mesh ', 'Graphics.', 'RenderTexture', 'GameObject', 'Transform',
                  'Rigidbody', 'KSPUtil', 'FlightGlobals', 'AcquireMesh(', 'SetUVs(',
                  'UploadMeshData(', 'DrawMeshNow'):
    ck(forbidden not in buildprep,
       'worker BuildManagedPreparation avoids Unity/KSP object API: '+forbidden)
ck(buildprep and 'new Vector3[vertexCount]' in buildprep and
   'BuildManagedLine(' in buildprep and 'BuildGpuGeographicAttribute(' in buildprep and
   'Math.Cos' in R and 'Math.Sin' in R,
   'large source/line/geographic managed preparation is constructed on worker')
ck(advance and 'case PendingEntryCommitStage.ClipTriangles:' in advance and
   'pending.Stage = PendingEntryCommitStage.SubmitManagedPreparation;' in advance and
   'case PendingEntryCommitStage.WaitManagedPreparation:' in advance and
   'ApplyManagedPreparation(pending);' in advance and
   'pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' in advance,
   'hot path is clip -> submit/wait worker -> Unity Mesh upload')
ck('AdvancePendingGeographic(' not in advance and
   'PreparePendingSources(' not in advance and
   'PreparePendingPackedTerrain(' not in advance,
   'rev003 allocation-heavy main-thread preparation/geographic methods are bypassed')
ck(applyprep and 'pending.PackedGpuGeographicAttribute = payload.PackedGpuGeographicAttribute;' in applyprep and
   'pending.ContourGpuGeographicAttribute = payload.ContourGpuGeographicAttribute;' in applyprep and
   'pending.CoastlineGpuGeographicAttribute = payload.CoastlineGpuGeographicAttribute;' in applyprep,
   'worker-prepared TEXCOORD1 payload transfers by reference to pending commit')

ck(finalize and 'PackedTerrainGeographicPoints = null' in finalize and
   'PackedTerrainProjectedVertices = null' in finalize and
   'ContourGeographicPoints = null' in finalize and
   'CoastlineGeographicPoints = null' in finalize and
   'ContourProjectedVertices = null' in finalize and
   'CoastlineProjectedVertices = null' in finalize,
   'normal GPU path does not preallocate double geographic or CPU projected fallback arrays')
ck(finalize and 'CoastalLandCorrectionElevationMeters =\n                    result.CoastalLandCorrectionElevationMeters' in finalize and
   'CoastalLandCorrectionShade = result.CoastalLandCorrectionShade' in finalize and
   'Valid = result.Valid' in finalize and '.Clone()' not in finalize,
   'Finalize shares immutable worker-result arrays instead of cloning on main thread')
ck(fallback and 'BuildGeographicPoints(' in fallback and 'AllocateProjectedVertices(' in fallback and
   'operationHealthCpuFallbackLazyAllocations++' in fallback,
   'double-precision CPU projection buffers are allocated lazily only for fail-closed fallback')
ck(project and 'gpuVertexProjection.Active && EnsureGpuVertexProjectionAttributes(entry)' in project and
   project.find('EnsureCpuProjectionFallbackData(entry)') >
   project.find('EnsureGpuVertexProjectionAttributes(entry)'),
   'CPU fallback allocation occurs only after GPU exact path cannot remain authoritative')
ck(ensuregpu and 'UploadPreparedGpuGeographicAttribute' in ensuregpu and
   'PackedTerrainGpuGeographicAttribute' in ensuregpu and
   'entry.PackedTerrainGpuGeographicAttribute = null;' in ensuregpu,
   'GPU TEXCOORD1 consumes worker payload directly and releases temporary lists after upload')
ck('entry.PackedTerrainGpuGeographicAttribute != null ?' in R and
   'entry.ContourGpuGeographicAttribute != null ?' in R and
   'entry.CoastlineGpuGeographicAttribute != null ?' in R,
   'GPU reject diagnostics report prepared geographic lengths instead of false -1 values')

ck(cancel and 'runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute' in cancel and
   'landSurfaceScratch = new SurfaceBuilder();' in cancel and
   'waterSurfaceScratch = new SurfaceBuilder();' in cancel,
   'lifecycle cancellation detaches scratch ownership if a worker may still read it')
ck('SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();' in R and
   'SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();' in R and
   'readonly SurfaceBuilder landSurfaceScratch' not in R,
   'renderer scratch builders can be safely detached on rare cancellation race')

non_tick_start=R.find('            if (!authoritativeTickDue)')
non_tick_end=R.find('            operationHealthAuthoritativeTicks++;',non_tick_start)
non_tick=R[non_tick_start:non_tick_end] if non_tick_start>=0 and non_tick_end>non_tick_start else ''
ck('PumpStagedCompletedCommit(system, false);' in non_tick and
   'CaptureVisible(' not in non_tick and 'RenderBackBuffer(' not in non_tick,
   'hidden frames retain rev003 prepare-only/no-visible-publication contract')
ck(advance and 'if (!allowPublication)' in advance and
   'FinalizePendingEntryCommit(pending, system);' in advance,
   'Finalize remains gated by authoritative publication permission')
ck(finalize and 'DetachEntryForDeferredRetirement(old);' in finalize and
   release and 'presentationEntryPins.Contains(entry)' in release,
   'rev003 deferred retirement remains the only replacement Mesh lifetime path')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'visible ND presentation authority remains fixed 10 Hz')

for field in ('oh_managed_prep_submitted=','oh_managed_prep_completed=',
              'oh_managed_prep_rejected=','oh_managed_prep_failed=',
              'oh_managed_prep_worker_max_ms=','oh_managed_prep_bytes_peak=',
              'oh_managed_prep_bytes_total=','oh_managed_prep_scratch_detach=',
              'oh_cpu_fallback_lazy_alloc=','oh_cpu_fallback_lazy_bytes=',
              'oh_snapshot_stale_mesh=','oh_main_commit_geo_max_ms=',
              'oh_deferred_retire_pending='):
    ck(field in R,'runtime telemetry publishes '+field[:-1])
ck('operationHealthManagedPrepBytesTotal += allocatedBytes;' in R,
   'managed-preparation allocation volume is accumulated for GC correlation')

ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'presentationEntryPins.Contains(entry)' in R,
   'ADENOSINE packets/O(1) snapshot pin authority remain intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R,
   'rev008 snapshot Mesh lifetime guard remains intact')
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and 'oh_heading_plan_coalesced=' in R,
   'ATROPINE rev009 governor/coalescer remains intact')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' not in SH,
   'rev004 changes no shader equations or shader bytes')
ck('OH_PHASE6_004' in P5V and
   'verify_aeris25_managed_preparation_pipeline_hotfix.py' in P5V,
   'ADENOSINE inherited verifier explicitly admits exact rev004 descendant')
active='\n'.join(line for line in U.splitlines()
                 if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('verify_aeris25_managed_preparation_pipeline_hotfix.py' in active and
   'verify_aeris25_authoritative_publication_lifetime_hotfix.py' not in active,
   'rev004 build uses exactly one final-tree Phase6 verifier')

frozen=['Source/AERISFlightControl/AA','Source/AERISFlightControl/Autopilot',
        'Source/AERISFlightControl/Protect','Source/AERISFlightControl/Landing']
try:
    changed=subprocess.check_output(['git','-C',str(ROOT),'diff','--name-only','HEAD','--']+frozen,
                                    text=True).strip().splitlines()
except Exception:
    changed=['GIT_DIFF_UNAVAILABLE']
ck(changed==[],'AA/AP/PROTECT/LAND working-tree edits remain NONE')
failed=[n for ok,n in checks if not ok]
print('\n[AERIS25 NOREPINEPHRINE PHASE6_004 MANAGED PREPARATION PIPELINE] %d/%d PASS' %
      (len(checks)-len(failed),len(checks)))
if failed:
    msg='; '.join(failed); print('FAILED: '+msg)
    print('::error title=NOREPINEPHRINE Phase6_004 verifier::'+msg)
    raise SystemExit(1)
print('[AERIS25 NOREPINEPHRINE PHASE6_004 MANAGED PREPARATION PIPELINE] STATIC PASS')
