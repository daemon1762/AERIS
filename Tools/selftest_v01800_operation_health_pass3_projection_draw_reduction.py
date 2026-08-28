#!/usr/bin/env python3
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n):
    checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)

ck('static readonly Bounds NdPresentationBounds' in R,
   'conservative fixed ND bounds authority exists')
ck('static void EnsureProjectedGeometry' not in R and
   'static void ProjectMesh' not in R and
   'static void EnsureWaterColour' not in R,
   'Pass 3 instance telemetry paths are not declared static')
proj=R[R.index('void ProjectMesh('):R.index('static double UnitLatitude')]
ck('RecalculateBounds()' not in proj and 'operationHealthBoundsSkips++' in proj,
   'projection updates no longer rescan mesh bounds')
ck('mesh.bounds = NdPresentationBounds' in R,
   'new/recycled meshes receive conservative fixed bounds')
ck('Dictionary<int, int[]> identityIndexCache' in R and
   'GetIdentityIndices(vertexCount)' in R,
   'identity index arrays are cached by vertex count')
ck('Dictionary<int, Color32[]> uniformColourScratch' in R and
   'GetUniformColourScratch(mesh.vertexCount, colour)' in R,
   'uniform water-colour upload scratch is reusable')
draw=R[R.index('bool DrawEntry('):R.index('void EnsureWaterColour',R.index('bool DrawEntry('))]
legacy_setpass = draw.count('terrainMaterial.SetPass(0)') == 1
packed_setpass = (
    'Material terrainDrawMaterial = gpuEntry ? gpuVertexProjection.TerrainMaterial : terrainMaterial;' in draw and
    draw.count('terrainDrawMaterial.SetPass(0)') == 1 and
    'Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)' in draw
)
ck(legacy_setpass or packed_setpass,
   'terrain surfaces share one material SetPass per entry through legacy or accepted packed descendant')
# Legacy used four distinct terrain meshes in painter order. The accepted packed
# descendant preserves exactly that semantic order in one mesh by assigning contiguous
# offsets and writing indices water -> land -> coastal water -> coastal land.
legacy_order=[
 'Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)'
]
legacy_pos=[draw.find(x) for x in legacy_order]
legacy_painter=all(x>=0 for x in legacy_pos) and legacy_pos==sorted(legacy_pos)
packed_tokens=[
 'pending.PackedWaterOffset = 0;',
 'pending.PackedLandOffset = pending.PackedWaterCount;',
 'pending.PackedCoastalWaterOffset = pending.PackedLandOffset +',
 'pending.PackedCoastalLandOffset = pending.PackedCoastalWaterOffset +'
]
packed_pos=[R.find(x) for x in packed_tokens]
packed_painter=(
    all(x>=0 for x in packed_pos) and packed_pos==sorted(packed_pos) and
    'pending.PackedIndices[pending.PackedIndexWriteCursor++] =\n                                pending.PackedWaterOffset +' in R and
    'pending.PackedIndices[pending.PackedIndexWriteCursor++] =\n                                pending.PackedLandOffset +' in R and
    'pending.PackedIndices[pending.PackedIndexWriteCursor++] =\n                                pending.PackedCoastalWaterOffset + pending.PrepareCursor++;' in R and
    'pending.PackedIndices[pending.PackedIndexWriteCursor++] =\n                                pending.PackedCoastalLandOffset + pending.PrepareCursor++;' in R and
    'Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)' in draw
)
ck(legacy_painter or packed_painter,
   'Candidate8 terrain painter order remains unchanged through legacy or accepted packed mesh descendant')
ck('oh_bounds_skip=' in R and 'oh_setpass_saved=' in R and
   'oh_identity_index_hit=' in R and 'oh_uniform_colour_reuse=' in R,
   'Pass 3 runtime telemetry is published')
ck('MaximumPooledMeshes = 24' in R,
   'Pass 2 bounded mesh pool remains 24')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'render target quality authority remains ARGB32 Bilinear')
ck(('nextBackRefreshRealtime = Time.realtimeSinceStartup + 0.10f' in R) or
   ('nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime' in R and
    'nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R),
   'terrain BACK cadence remains 0.10 seconds or approved shared 10 Hz authority')
ck('ProjectionRefreshAgeSeconds = 0.50f' in R and
   'ProjectionRefreshHeadingDeg = 8f' in R,
   'projection refresh thresholds remain unchanged')
ck('MaximumPooledMeshes = 24' in R and 'DestroyMeshPool();' in R,
   'explicit GPU release still destroys pooled meshes')

failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
