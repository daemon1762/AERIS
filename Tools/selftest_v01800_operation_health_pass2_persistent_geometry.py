#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
renderer=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text(encoding='utf-8')
raster=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(encoding='utf-8')
nav=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text(encoding='utf-8')

checks=[]
def check(label, ok):
    checks.append((label,bool(ok)))
    print(('[PASS] ' if ok else '[FAIL] ')+label)

check('SurfaceBuilder has reusable Reset path', 'internal void Reset()' in renderer and 'Vertices.Clear();' in renderer and 'Triangles.Clear();' in renderer)
check('BuildEntry reuses persistent land/water builders', 'SurfaceBuilder land = landSurfaceScratch;' in renderer and 'SurfaceBuilder water = waterSurfaceScratch;' in renderer)
check('BuildEntry reuses one clipping scratch buffer', 'SurfacePoint[] clipped = surfaceClipScratch;' in renderer)
check('per-triangle SurfacePoint input array allocation removed', 'SurfacePoint[] input = { a, b, c };' not in renderer)
check('native Mesh pool exists', 'readonly Queue<Mesh> meshPool' in renderer and 'Mesh AcquireMesh(' in renderer)
check('native Mesh pool is tightly bounded', 'const int MaximumPooledMeshes = 24;' in renderer)
legacy_remove = 'RecycleMesh(ref entry.LandMesh);' in renderer and 'RecycleMesh(ref entry.CoastlineMesh);' in renderer
packed_remove = (
    'RecycleMesh(ref entry.PackedTerrainMesh);' in renderer and
    'RecycleMesh(ref entry.ContourMesh);' in renderer and
    'RecycleMesh(ref entry.CoastlineMesh);' in renderer and
    'void ReleaseDeferredEntryRetirements(bool force)' in renderer and
    'if (!force && presentationEntryPins.Contains(entry))' in renderer
)
check('ordinary entry removal recycles meshes through legacy or accepted packed snapshot-safe descendant', legacy_remove or packed_remove)
check('terrain suspension destroys pooled GPU resources', 'DestroyMeshPool();' in renderer and 'void ReleaseGpuResources()' in renderer)
check('mesh-pool telemetry is published', 'oh_mesh_pool_hit=' in renderer and 'oh_mesh_recycle=' in renderer and 'oh_surface_builder_reuse=' in renderer)
check('Standard water colour avoids redundant first upload', 'WaterColourPreset = AERISTerrainColourPreset.Standard' in renderer)
check('raster topology uses exact one-shot index allocation', 'int[] triangles = BuildTriangleIndices(valid, resolution);' in raster and 'static int[] BuildTriangleIndices' in raster)
check('legacy triangle List plus ToArray copy removed', 'var triangles = new List<int>' not in raster and 'Triangles = triangles.ToArray()' not in raster)
check('coastal correction input/clip buffers are build-scoped', 'var correctionInput = new CorrectionPoint[3];' in raster and 'var correctionClip = new CorrectionPoint[6];' in raster)
check('per-polygon coastal clip allocation removed', 'var clipped = new CorrectionPoint[6]' not in raster)
check('CancelAll scheduler-key List is reused', 'cancelSchedulerKeysScratch' in raster and 'var schedulerKeys = new List<string>()' not in raster)
check('Candidate11 contour level budget remains 96', 'const int MaximumContourLevelsPerTile = 96;' in raster)
check('Candidate11 coastal contour clipping remains enabled', 'HighDensityBoundaryCrossesParentCell' in raster and 'AppendContourSegment' in raster)
check('sparse coastal parent safety rail remains 256', 'const int MaximumSparseCorrectionParentCells = 256;' in raster)
check('ARGB32 FRONT/BACK format unchanged', 'RenderTextureFormat.ARGB32' in renderer)
check('Bilinear render-target filtering unchanged', 'filterMode = FilterMode.Bilinear' in renderer)
check('terrain BACK cadence remains 0.10 seconds or approved shared 10 Hz authority',
      ('nextBackRefreshRealtime = Time.realtimeSinceStartup + 0.10f;' in renderer) or
      ('nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime;' in renderer and
       'nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f;' in renderer))
legacy_projection = (
      'bool exactProjectionDue =' in renderer and 'if (!exactProjectionDue)' in renderer and
      'Matrix4x4.Translate' in renderer and
      'ProjectMesh(entry.LandMesh' in renderer and
      renderer.index('if (!exactProjectionDue)') < renderer.index('ProjectMesh(entry.LandMesh')
)
packed_projection = (
      'bool exactProjectionDue =' in renderer and 'if (!exactProjectionDue)' in renderer and
      'Matrix4x4.Translate' in renderer and
      'ProjectMesh(entry.PackedTerrainMesh,' in renderer and
      'entry.PackedTerrainGeographicPoints,' in renderer and
      'entry.PackedTerrainProjectedVertices, context);' in renderer and
      renderer.index('if (!exactProjectionDue)') < renderer.index('ProjectMesh(entry.PackedTerrainMesh,')
)
check('projection geometry still updates only when exact-dirty while subpixel motion avoids vertex rewrite through legacy or accepted packed descendant', legacy_projection or packed_projection)
check('Operation Health Hotfix 1 FRONT-synchronized symbology retained', 'terrainSymbologyFrontSwap' in nav and 'terrainTileRenderer.FrontBufferSwaps' in nav)
check('Hotfix 1 prediction vector retains synchronized speed/track', 'terrainSymbologyGroundSpeedMps' in nav and 'terrainSymbologyGroundTrackDeg' in nav)

passed=sum(1 for _,ok in checks if ok)
print('\n[Operation Health Pass 2] %d/%d PASS' % (passed,len(checks)))
raise SystemExit(0 if passed==len(checks) else 1)