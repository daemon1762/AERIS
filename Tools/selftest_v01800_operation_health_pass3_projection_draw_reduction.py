#!/usr/bin/env python3
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
F=''.join(R.split())
checks=[]
def ck(v,n):
    checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('staticreadonlyBoundsNdPresentationBounds' in F,'conservative fixed ND bounds authority exists')
ck('staticvoidEnsureProjectedGeometry' not in F and 'staticvoidProjectMesh' not in F and 'staticvoidEnsureWaterColour' not in F,'Pass 3 instance telemetry paths are not declared static')
proj=R[R.index('void ProjectMesh('):R.index('static double UnitLatitude')]
ck('RecalculateBounds()' not in proj and 'operationHealthBoundsSkips++' in proj,'projection updates no longer rescan mesh bounds')
ck('mesh.bounds=NdPresentationBounds' in F,'new/recycled meshes receive conservative fixed bounds')
ck('Dictionary<int,int[]>identityIndexCache' in F and 'GetIdentityIndices(vertexCount)' in R,'identity index arrays are cached by vertex count')
ck('Dictionary<int,Color32[]>uniformColourScratch' in F and 'GetUniformColourScratch(mesh.vertexCount,colour)' in F,'uniform water-colour upload scratch is reusable')
draw=R[R.index('bool DrawEntry('):R.index('void EnsureWaterColour',R.index('bool DrawEntry('))]
DF=''.join(draw.split())
ck(DF.count('terrainMaterial.SetPass(0)') == 1,'terrain meshes share one material SetPass per entry')
order=[
 'Graphics.DrawMeshNow(entry.WaterMesh,mapMatrix)',
 'Graphics.DrawMeshNow(entry.LandMesh,mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh,mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh,mapMatrix)']
pos=[DF.find(x) for x in order]
ck(all(x>=0 for x in pos) and pos==sorted(pos),'Candidate8 terrain painter order remains unchanged')
ck('oh_bounds_skip=' in R and 'oh_setpass_saved=' in R and 'oh_identity_index_hit=' in R and 'oh_uniform_colour_reuse=' in R,'Pass 3 runtime telemetry is published')
ck('MaximumPooledMeshes=24' in F,'Pass 2 bounded mesh pool remains 24')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render target quality authority remains ARGB32 Bilinear')
ck(('nextBackRefreshRealtime=Time.realtimeSinceStartup+0.10f' in F) or
   ('nextBackRefreshRealtime=nextAuthoritativePresentationTickRealtime' in F and
    'nextAuthoritativePresentationTickRealtime=presentationNow+0.10f' in F),
   'terrain BACK cadence remains 0.10 seconds or approved shared 10 Hz authority')
ck('ProjectionRefreshAgeSeconds=0.50f' in F and 'ProjectionRefreshHeadingDeg=8f' in F,'projection refresh thresholds remain unchanged')
ck('MaximumPooledMeshes=24' in F and 'DestroyMeshPool();' in R,'explicit GPU release still destroys pooled meshes')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
