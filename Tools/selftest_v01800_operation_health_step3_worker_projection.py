#!/usr/bin/env python3
from pathlib import Path
import json,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
T=ROOT/'Source/AERISFlightControl/Terrain'
R=(T/'AERISTerrainGpuTileRenderer.cs').read_text(); RF=''.join(R.split())
W=(T/'AERISTerrainGpuTileRenderer.WorkerProjection.cs').read_text(); WF=''.join(W.split())
P=(ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
RA=(T/'AERISTerrainGpuTileRasterizer.cs').read_text(); C=(T/'AERISTerrainCoastlineExtractor.cs').read_text()
MP=(T/'AERISNdMapProjection.cs').read_text()
B=(ROOT/'build_ubuntu.sh').read_text(); V=json.loads((ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text())
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('internalsealedpartialclassAERISTerrainGpuTileRenderer' in RF and 'internalsealedpartialclassAERISTerrainGpuTileRenderer' in WF,'renderer is split into explicit Worker Projection partial')
ck('Terrain\\AERISTerrainGpuTileRenderer.WorkerProjection.cs' in P,'Worker Projection partial is compiled by csproj')
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,'authoritative ND sampling remains fixed 10 Hz')
ck('ProjectionWorkerJobKey="nd-terrain-exact-projection"' in WF and 'SubmitRequired(' in W and 'AERISRuntimeLane.GeneralCompute' in W,'exact projection uses bounded central scheduler GeneralCompute lane')
ck('ProjectUnitToRenderNUp(point.X,point.Y,point.Z' in WF,'worker uses the exact existing projection equation')
worker=W[W.index('// Pure worker section.'):W.index('void CompleteProjectionWorker')]
for forbidden in ('Mesh', 'Material', 'RenderTexture', 'Transform', 'Vessel', 'CelestialBody', 'Graphics.', 'GL.', 'Vector3', 'UnityEngine.Object'):
    ck(forbidden not in worker,'worker math contains no Unity/KSP native/value API: '+forbidden)
ck('ProjectionPlaneBuffer destination' in worker and 'destination.U[i] = u;' in worker and 'destination.V[i] = v;' in worker,'worker output is plain float U/V arrays only')
project_method=MP[MP.index('internal void ProjectUnitToRenderNUp'):MP.index('internal Matrix4x4 ResolveScaleCorrectedRenderMatrix')]
local_method=MP[MP.index('void ProjectUnitToLocalMeters'):MP.index('    }\n\n    internal sealed class AERISNdMapLockReference')]
ck('Mathf.' not in project_method and 'Mathf.' not in local_method and 'Matrix4x4' not in project_method and 'Vector3' not in project_method,'worker projection call chain is System.Math/scalar only')
ck('!forceCenterProjectionRefresh||contentTickRequired' in WF and 'colourRefreshRequired' in W,'worker is restricted to stable motion-only exact refresh')
ck('!frontBufferValid||!requestedViewReady' in WF and 'frontContentRevision!=gpuContentRevision' in WF,'initial/loading/dirty views stay on existing main-thread exact path')
ck('operationHealthProjectionWorkerFallbacks++' in R and 'RenderBackBuffer(tiles,drawEntriesScratch,projection' in RF,'busy or unavailable worker falls back to existing exact renderer')
ck('BaseFrontBufferSwaps' in W and 'request.BaseFrontBufferSwaps!=frontBufferSwaps' in WF,'late worker result cannot overwrite a newer FRONT')
ck('request.ContentRevision!=gpuContentRevision' in WF and 'request.TerrainGeneration!=contentVisible.TerrainGeneration' in WF and 'request.ViewGeneration!=contentVisible.ViewGeneration' in WF,'worker result is generation and content revision guarded')
ck('ProjectionWorkerBuffersMatchCurrentEntries' in W and 'ProjectionWorkerBuffersMatch(entry, buffers)' in W,'all entry and mesh buffers are prevalidated before native upload')
preupload=W.index('// Validate the whole presentation set before changing a single native Mesh.')
upload=W.index('ApplyProjectionWorkerMesh(entry.LandMesh',preupload); validate=W.index('ProjectionWorkerBuffersMatch(entry, buffers)',preupload)
ck(validate < upload,'atomic validation precedes every worker Mesh upload')
ck('MeasureRunwayMapLockError(plot, request.Projection' in W and 'if (runwayError > 1.0f)' in W,'Runway Map Lock is revalidated before worker FRONT commit')
ck('ProjectionWorkerMinimumCommitIntervalSeconds=0.10f' in WF and 'Time.realtimeSinceStartup-frontCommittedRealtime<ProjectionWorkerMinimumCommitIntervalSeconds' in WF,'async result cannot create sub-100ms FRONT swap bursts')
commit_start=WF.index('boolTryCommitProjectionWorkerResult'); commit_end=WF.index('boolProjectionWorkerResultStillCurrent')
ck(WF.index('Time.realtimeSinceStartup-frontCommittedRealtime<ProjectionWorkerMinimumCommitIntervalSeconds',commit_start) < WF.index('projectionWorkerCompleted=null;',commit_start,commit_end),'cadence-deferred completed result is retained rather than discarded')
ck('RenderBackBuffer(sortedTilesScratch,drawEntriesScratch' in WF and 'request.RangeMeters,false)' in WF,'worker result reuses existing BACK renderer without reprojecting vertices')
ck('SwapFrontAndBack(contentVisible,vessel' in WF,'worker result enters existing atomic FRONT swap authority')
ck('TryCommitProjectionWorkerResult(plot,vessel,lockReference)' in RF and 'TrySubmitProjectionWorker(visible,projection' in RF,'Draw consumes completed worker result and schedules next exact projection')
ck('oh_project_worker_submit=' in W and 'oh_project_worker_commit=' in W and 'oh_project_worker_fallback=' in W and 'oh_project_worker_stale=' in W and 'oh_project_worker_defer=' in W and 'project_worker_buffer_bytes=' in W and 'project_worker_ms=' in W,'runtime Worker Projection telemetry is published')
ck('ContentMaintenanceRetrySeconds=0.20f' in RF and 'NeedsContentRefresh(' in R and 'operationHealthMotionOnlyTicks++' in R,'Step 2 Motion/Content split remains present')
ck('MaximumSparseCorrectionParentCells = 256' in RA and 'MaximumContourLevelsPerTile = 96' in RA,'Candidate11 coastal and contour authorities remain unchanged')
ck('HighDensityResolution = 129' in C,'129x129 HD coastline authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render target quality remains ARGB32 Bilinear')
ck(V.get('NAME') == 'AERISFlightControl DEV CP3.75 Operation Health Step 3 Worker Projection','runtime identity is Step 3 Worker Projection')
ck('OPERATION HEALTH STEP 3 WORKER PROJECTION' in B,'Ubuntu build identifies Step 3 Worker Projection')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Step 3 Worker Projection] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
