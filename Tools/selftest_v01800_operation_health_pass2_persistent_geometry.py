#!/usr/bin/env python3
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
renderer=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text(encoding='utf-8')
F=''.join(renderer.split())
raster=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(encoding='utf-8')
nav=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text(encoding='utf-8')
checks=[]
def check(label,ok): checks.append((label,bool(ok))); print(('[PASS] ' if ok else '[FAIL] ')+label)
check('SurfaceBuilder has reusable Reset path','internalvoidReset()' in F and 'Vertices.Clear();' in renderer and 'Triangles.Clear();' in renderer)
check('BuildEntry reuses persistent land/water builders','SurfaceBuilderland=landSurfaceScratch' in F and 'SurfaceBuilderland=landSurfaceScratch,water=waterSurfaceScratch' in F)
check('BuildEntry reuses one clipping scratch buffer','SurfacePoint[]clipped=surfaceClipScratch' in F)
check('per-triangle SurfacePoint input array allocation removed','SurfacePoint[]input={a,b,c};' not in F)
check('native Mesh pool exists','readonlyQueue<Mesh>meshPool' in F and 'MeshAcquireMesh(' in F)
check('native Mesh pool is tightly bounded','constintMaximumPooledMeshes=24;' in F)
check('ordinary entry removal recycles meshes','RecycleMesh(refentry.LandMesh);' in F and 'RecycleMesh(refentry.CoastlineMesh);' in F)
check('terrain suspension destroys pooled GPU resources','DestroyMeshPool();' in renderer and 'voidReleaseGpuResources()' in F)
check('mesh-pool telemetry is published','oh_mesh_pool_hit=' in renderer and 'oh_mesh_recycle=' in renderer and 'oh_surface_builder_reuse=' in renderer)
check('Standard water colour avoids redundant first upload','WaterColourPreset=AERISTerrainColourPreset.Standard' in F)
check('raster topology uses exact one-shot index allocation','int[] triangles = BuildTriangleIndices(valid, resolution);' in raster and 'static int[] BuildTriangleIndices' in raster)
check('legacy triangle List plus ToArray copy removed','var triangles = new List<int>' not in raster and 'Triangles = triangles.ToArray()' not in raster)
check('coastal correction input/clip buffers are build-scoped','var correctionInput = new CorrectionPoint[3];' in raster and 'var correctionClip = new CorrectionPoint[6];' in raster)
check('per-polygon coastal clip allocation removed','var clipped = new CorrectionPoint[6]' not in raster)
check('CancelAll scheduler-key List is reused','cancelSchedulerKeysScratch' in raster and 'var schedulerKeys = new List<string>()' not in raster)
check('Candidate11 contour level budget remains 96','const int MaximumContourLevelsPerTile = 96;' in raster)
check('Candidate11 coastal contour clipping remains enabled','HighDensityBoundaryCrossesParentCell' in raster and 'AppendContourSegment' in raster)
check('sparse coastal parent safety rail remains 256','const int MaximumSparseCorrectionParentCells = 256;' in raster)
check('ARGB32 FRONT/BACK format unchanged','RenderTextureFormat.ARGB32' in renderer)
check('Bilinear render-target filtering unchanged','FilterMode.Bilinear' in renderer)
check('terrain BACK cadence remains 0.10 seconds or approved shared 10 Hz authority',
      ('nextBackRefreshRealtime=Time.realtimeSinceStartup+0.10f;' in F) or
      ('nextBackRefreshRealtime=nextAuthoritativePresentationTickRealtime;' in F and 'nextAuthoritativePresentationTickRealtime=presentationNow+0.10f;' in F))
check('projection geometry still updates only when dirty','if(!projectionChanged)return;' in F and 'EnsureProjectedGeometry' in renderer)
check('Operation Health Hotfix 1 FRONT-synchronized symbology retained','terrainSymbologyFrontSwap' in nav and 'terrainTileRenderer.FrontBufferSwaps' in nav)
check('Hotfix 1 prediction vector retains synchronized speed/track','terrainSymbologyGroundSpeedMps' in nav and 'terrainSymbologyGroundTrackDeg' in nav)
passed=sum(1 for _,ok in checks if ok)
print('\n[Operation Health Pass 2] %d/%d PASS' % (passed,len(checks)))
raise SystemExit(0 if passed==len(checks) else 1)
