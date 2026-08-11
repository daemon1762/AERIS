#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
renderer_path = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
if not renderer_path.is_file():
    raise SystemExit('[AERIS23 CANDIDATE VERIFY] renderer source not found')

r = renderer_path.read_text()

def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit('[AERIS23 CANDIDATE VERIFY] FAIL: ' + message)
    print('[PASS] ' + message)

require('oh_terrain_single_build=' in r,
        'Single-Authority runtime telemetry is present')
require('BuildTriangleSourceVertices(' in r,
        'coastal correction source is CPU-only before packing')
require('PackedTerrainMesh' in r and 'BuildPackedTerrainMesh(' in r,
        'PackedTerrainMesh is the terrain GPU authority')

start = r.find('        Entry BuildEntry(')
end = r.find('        static SurfacePoint Point(', start)
require(start >= 0 and end > start, 'BuildEntry section is discoverable')
build = r[start:end]
require('BuildSurfaceMesh(' not in build,
        'BuildEntry does not create legacy Land/Water Unity Mesh objects')
require('BuildTriangleListMesh(' not in build,
        'BuildEntry does not create legacy coastal correction Unity Mesh objects')
for forbidden in (
    'LandMesh =', 'WaterMesh =', 'CoastalLandCorrectionMesh =',
    'CoastalWaterCorrectionMesh ='):
    require(forbidden not in build,
            'BuildEntry does not assign legacy terrain authority: ' + forbidden)
require('PackedTerrainMesh = packedTerrainMesh' in build,
        'BuildEntry assigns the packed terrain mesh exactly once')

project_start = r.find('        Matrix4x4 EnsureProjectedGeometry(')
project_end = r.find('        void ProjectMesh(', project_start)
require(project_start >= 0 and project_end > project_start,
        'projection section is discoverable')
project = r[project_start:project_end]
require('ProjectMesh(entry.PackedTerrainMesh' in project,
        'exact projection uploads the packed terrain mesh')
for legacy in (
    'ProjectMesh(entry.LandMesh', 'ProjectMesh(entry.WaterMesh',
    'ProjectMesh(entry.CoastalLandCorrectionMesh',
    'ProjectMesh(entry.CoastalWaterCorrectionMesh'):
    require(legacy not in project,
            'exact projection has no legacy terrain fallback: ' + legacy)

draw_start = r.find('        bool DrawEntry(')
draw_end = r.find('        static void EnsurePackedTerrainColours(', draw_start)
require(draw_start >= 0 and draw_end > draw_start, 'DrawEntry section is discoverable')
draw = r[draw_start:draw_end]
require(draw.count('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') == 1,
        'terrain submission is exactly one DrawMeshNow per Entry')
require('Graphics.DrawMeshNow(entry.LandMesh' not in draw and
        'Graphics.DrawMeshNow(entry.WaterMesh' not in draw,
        'DrawEntry has no legacy terrain draw fallback')
require(draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') <
        draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)') <
        draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)'),
        'Entry painter order remains terrain -> contour -> coastline')

remove_start = r.find('        void Remove(Entry entry)')
remove_end = r.find('        void FailGpuTerrain(', remove_start)
require(remove_start >= 0 and remove_end > remove_start, 'Entry recycle section is discoverable')
remove = r[remove_start:remove_end]
require('RecycleMesh(ref entry.PackedTerrainMesh)' in remove,
        'Entry lifecycle recycles PackedTerrainMesh')
for legacy in (
    'RecycleMesh(ref entry.LandMesh)', 'RecycleMesh(ref entry.WaterMesh)',
    'RecycleMesh(ref entry.CoastalLandCorrectionMesh)',
    'RecycleMesh(ref entry.CoastalWaterCorrectionMesh)'):
    require(legacy not in remove,
            'Entry recycle has no legacy terrain mesh: ' + legacy)

require('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in r,
        'fixed 10 Hz presentation authority remains intact')
require('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
        'ARGB32/Bilinear visual authority remains intact')

print('[AERIS23 CANDIDATE VERIFY] SINGLE_AUTHORITY_TERRAIN_PACK SOURCE PASS')
