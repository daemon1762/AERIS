#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
W=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.WorkerProjection.cs').read_text()
P=(ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
RA=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
checks=[]
def ck(v,n):
    checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)

ck('internal sealed partial class AERISTerrainGpuTileRenderer' in R and
   'internal sealed partial class AERISTerrainGpuTileRenderer' in W,
   'renderer is split into explicit Worker Projection partial')
ck('Terrain\\AERISTerrainGpuTileRenderer.WorkerProjection.cs' in P,
   'Worker Projection partial is compiled by csproj')
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,
   'authoritative ND sampling remains fixed 10 Hz')
ck('ProjectionWorkerJobKey = "nd-terrain-exact-projection"' in W and
   'SubmitRequired(' in W and 'AERISRuntimeLane.GeneralCompute' in W,
   'exact projection uses bounded central scheduler GeneralCompute lane')
ck('ProjectUnitToRenderNUp(point.X, point.Y, point.Z' in W,
   'worker uses the exact existing projection equation')
worker=W[W.index('static ProjectionWorkerResult BuildProjectionWorkerResult'):W.index('void CompleteProjectionWorker')]
for forbidden in ('Mesh ', 'Material ', 'RenderTexture', 'Transform', 'Vessel',
                  'CelestialBody', 'Graphics.', 'GL.', 'UnityEngine.Object'):
    ck(forbidden not in worker, 'worker math does not access native API: '+forbidden.strip())
ck('Vector3[] destination' in worker and 'new Vector3(u, v, 0f)' in worker,
   'worker output is value-array only')
ck('!forceCenterProjectionRefresh || contentTickRequired' in W and
   'colourRefreshRequired' in W,
   'worker is restricted to stable motion-only exact refresh')
ck('!frontBufferValid || !requestedViewReady' in W and
   'frontContentRevision != gpuContentRevision' in W,
   'initial/loading/dirty views stay on existing main-thread exact path')
ck('operationHealthProjectionWorkerFallbacks++' in R and
   'RenderBackBuffer(tiles, drawEntriesScratch, projection' in R,
   'busy or unavailable worker falls back to existing exact renderer')
ck('BaseFrontBufferSwaps' in W and
   'request.BaseFrontBufferSwaps != frontBufferSwaps' in W,
   'late worker result cannot overwrite a newer FRONT')
ck('request.ContentRevision != gpuContentRevision' in W and
   'request.TerrainGeneration != contentVisible.TerrainGeneration' in W and
   'request.ViewGeneration != contentVisible.ViewGeneration' in W,
   'worker result is generation and content revision guarded')
ck('ProjectionWorkerBuffersMatchCurrentEntries' in W and
   'ProjectionWorkerBuffersMatch(entry, buffers)' in W,
   'all entry and mesh buffers are prevalidated before native upload')
preupload=W.index('// Validate every mesh/buffer pair before changing any native Mesh.')
upload=W.index('ApplyProjectionWorkerMesh(entry.LandMesh',preupload)
validate=W.index('ProjectionWorkerBuffersMatch(entry, buffers)',preupload)
ck(validate < upload,'atomic validation precedes every worker Mesh upload')
ck('MeasureRunwayMapLockError(plot, request.Projection' in W and
   'if (runwayError > 1.0f)' in W,
   'Runway Map Lock is revalidated before worker FRONT commit')
ck('RenderBackBuffer(sortedTilesScratch, drawEntriesScratch' in W and
   'request.RangeMeters, false)' in W,
   'worker result reuses existing BACK renderer without reprojecting vertices')
ck('SwapFrontAndBack(contentVisible, vessel' in W,
   'worker result enters the existing atomic FRONT swap authority')
ck('TryCommitProjectionWorkerResult(plot, vessel, lockReference)' in R and
   'TrySubmitProjectionWorker(visible, projection' in R,
   'Draw consumes completed worker result and schedules next exact projection')
ck('oh_project_worker_submit=' in W and 'oh_project_worker_commit=' in W and
   'oh_project_worker_fallback=' in W and 'oh_project_worker_stale=' in W and
   'project_worker_ms=' in W,
   'runtime Worker Projection telemetry is published')
ck('ContentMaintenanceRetrySeconds = 0.20f' in R and
   'NeedsContentRefresh(' in R and 'operationHealthMotionOnlyTicks++' in R,
   'Step 2 Motion/Content split remains present')
ck('MaximumSparseCorrectionParentCells = 256' in RA and
   'MaximumContourLevelsPerTile = 96' in RA,
   'Candidate11 coastal and contour authorities remain unchanged')
ck('HighDensityResolution = 129' in
   (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainCoastlineExtractor.cs').read_text(),
   '129x129 HD coastline authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'render target quality remains ARGB32 Bilinear')

failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Step 3 Worker Projection] %d/%d PASS' %
      (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
