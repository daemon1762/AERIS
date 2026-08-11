#!/usr/bin/env python3
from pathlib import Path
import subprocess, sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
legacy = ROOT / 'Tools/apply_aeris23_entry_terrain_mesh_packing_candidate.py'
if not legacy.exists():
    raise SystemExit('PR23 packing applicator is missing')

# Reuse the already-audited Entry-order-preserving packing transform, then remove its
# duplicate-resident safety fallback. The final local source has one terrain Unity Mesh
# authority per Entry, not five.
subprocess.run([sys.executable, str(legacy)], cwd=str(ROOT), check=True)

renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text = renderer.read_text()
if 'oh_terrain_single_build=' in text:
    print('[AERIS23 Single-Authority Terrain Pack] already applied')
    raise SystemExit(0)


def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old, new, 1)

# Runtime counter that must become nearly flat after READY. This directly catches the
# prune/rebuild storm that rejected the duplicate-resident PR23 implementation.
text = replace_once(text,
'''        long operationHealthTerrainSetPassSaved;
        long operationHealthPackedTerrainDraws;
        long operationHealthPackedTerrainDrawSubmissionsSaved;''',
'''        long operationHealthTerrainSetPassSaved;
        long operationHealthPackedTerrainBuilds;
        long operationHealthPackedTerrainDraws;
        long operationHealthPackedTerrainDrawSubmissionsSaved;''',
'packed build telemetry field')

# BuildEntry now creates CPU source arrays only. It never creates Land/Water/Coastal Unity
# Mesh objects before packing, so there is no transient or resident duplicate GPU authority.
old_sources = '''            Vector3[] landSource, waterSource, contourSource, coastlineSource;
            Mesh landMesh = BuildSurfaceMesh("AERIS_TERRAIN_LAND_" +
                result.Key.FileStem, land, false, out landSource);
            Mesh waterMesh = BuildSurfaceMesh("AERIS_TERRAIN_WATER_" +
                result.Key.FileStem, water, true, out waterSource);
            Vector3[] coastalLandCorrectionSource, coastalWaterCorrectionSource;
            Mesh coastalLandCorrectionMesh = BuildTriangleListMesh(
                "AERIS_TERRAIN_COAST_LAND_FIX_" + result.Key.FileStem,
                result.CoastalLandCorrectionVertices, false,
                out coastalLandCorrectionSource);
            Mesh coastalWaterCorrectionMesh = BuildTriangleListMesh(
                "AERIS_TERRAIN_COAST_WATER_FIX_" + result.Key.FileStem,
                result.CoastalWaterCorrectionVertices, true,
                out coastalWaterCorrectionSource);
            Vector3[] packedTerrainSource;'''
new_sources = '''            Vector3[] contourSource, coastlineSource;
            Vector3[] landSource = land.Vertices.Count <= 0 ? null : land.Vertices.ToArray();
            Vector3[] waterSource = water.Vertices.Count <= 0 ? null : water.Vertices.ToArray();
            Vector3[] coastalLandCorrectionSource = BuildTriangleSourceVertices(
                result.CoastalLandCorrectionVertices);
            Vector3[] coastalWaterCorrectionSource = BuildTriangleSourceVertices(
                result.CoastalWaterCorrectionVertices);
            Vector3[] packedTerrainSource;'''
text = replace_once(text, old_sources, new_sources,
    'BuildEntry removes legacy Unity terrain Mesh construction')

old_projected = '''            long projectedVertexBytes = (long)(land.Vertices.Count +
                water.Vertices.Count +
                (coastalLandCorrectionSource == null ? 0 : coastalLandCorrectionSource.Length) +
                (coastalWaterCorrectionSource == null ? 0 : coastalWaterCorrectionSource.Length) +
                (contourSource == null ? 0 : contourSource.Length) +
                (coastlineSource == null ? 0 : coastlineSource.Length)) * (3L * 8L + 3L * 4L);'''
new_projected = '''            long projectedVertexBytes = (long)(
                (packedTerrainSource == null ? 0 : packedTerrainSource.Length) +
                (contourSource == null ? 0 : contourSource.Length) +
                (coastlineSource == null ? 0 : coastlineSource.Length)) *
                (3L * 8L + 3L * 4L);'''
text = replace_once(text, old_projected, new_projected,
    'single-authority projected-state accounting')

old_bytes = '''            long bytes = result.Valid.Length + projectedVertexBytes +
                (packedTerrainSource == null ? 0L :
                    packedTerrainSource.LongLength * (3L * 8L + 3L * 4L + 3L * 4L + 4L) +
                    packedTerrainIndexCount * 4L) +
                land.Vertices.Count * (3L * 4L + 4L + 4L) +
                water.Vertices.Count * (3L * 4L + 4L) +
                (land.Triangles.Count + water.Triangles.Count) * 4L +
                (coastalLandCorrectionSource == null ? 0L :
                    coastalLandCorrectionSource.LongLength * (3L * 4L + 4L + 4L)) +
                (coastalWaterCorrectionSource == null ? 0L :
                    coastalWaterCorrectionSource.LongLength * (3L * 4L + 4L));'''
new_bytes = '''            long bytes = result.Valid.Length + projectedVertexBytes +
                (packedTerrainSource == null ? 0L :
                    packedTerrainSource.LongLength * (3L * 4L + 4L)) +
                packedTerrainIndexCount * 4L +
                land.Vertices.Count * (4L + 1L) +
                (result.CoastalLandCorrectionElevationMeters == null ? 0L :
                    result.CoastalLandCorrectionElevationMeters.LongLength * (4L + 1L));'''
text = replace_once(text, old_bytes, new_bytes,
    'remove duplicate-resident byte accounting')

old_entry = '''                LandMesh = landMesh,
                WaterMesh = waterMesh,
                PackedTerrainMesh = packedTerrainMesh,
                PackedTerrainGeographicPoints = BuildGeographicPoints(packedTerrainSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                PackedTerrainProjectedVertices = AllocateProjectedVertices(packedTerrainSource),
                PackedTerrainColours = packedTerrainColours,
                PackedWaterOffset = packedWaterOffset,
                PackedWaterCount = packedWaterCount,
                PackedLandOffset = packedLandOffset,
                PackedLandCount = packedLandCount,
                PackedCoastalWaterOffset = packedCoastalWaterOffset,
                PackedCoastalWaterCount = packedCoastalWaterCount,
                PackedCoastalLandOffset = packedCoastalLandOffset,
                PackedCoastalLandCount = packedCoastalLandCount,
                PackedTerrainSourceMeshCount = packedTerrainSourceMeshCount,
                CoastalLandCorrectionMesh = coastalLandCorrectionMesh,
                CoastalWaterCorrectionMesh = coastalWaterCorrectionMesh,
                ContourMesh = contourMesh,
                CoastlineMesh = coastlineMesh,
                LandGeographicPoints = BuildGeographicPoints(landSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                WaterGeographicPoints = BuildGeographicPoints(waterSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastalLandCorrectionGeographicPoints = BuildGeographicPoints(
                    coastalLandCorrectionSource, result.SouthLatitudeDeg,
                    result.NorthLatitudeDeg, result.WestLongitudeDeg,
                    result.EastLongitudeDeg),
                CoastalWaterCorrectionGeographicPoints = BuildGeographicPoints(
                    coastalWaterCorrectionSource, result.SouthLatitudeDeg,
                    result.NorthLatitudeDeg, result.WestLongitudeDeg,
                    result.EastLongitudeDeg),
                ContourGeographicPoints = BuildGeographicPoints(contourSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastlineGeographicPoints = BuildGeographicPoints(coastlineSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                LandProjectedVertices = AllocateProjectedVertices(landSource),
                WaterProjectedVertices = AllocateProjectedVertices(waterSource),
                CoastalLandCorrectionProjectedVertices =
                    AllocateProjectedVertices(coastalLandCorrectionSource),
                CoastalWaterCorrectionProjectedVertices =
                    AllocateProjectedVertices(coastalWaterCorrectionSource),
                ContourProjectedVertices = AllocateProjectedVertices(contourSource),
                CoastlineProjectedVertices = AllocateProjectedVertices(coastlineSource),'''
new_entry = '''                PackedTerrainMesh = packedTerrainMesh,
                PackedTerrainGeographicPoints = BuildGeographicPoints(packedTerrainSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                PackedTerrainProjectedVertices = AllocateProjectedVertices(packedTerrainSource),
                PackedTerrainColours = packedTerrainColours,
                PackedWaterOffset = packedWaterOffset,
                PackedWaterCount = packedWaterCount,
                PackedLandOffset = packedLandOffset,
                PackedLandCount = packedLandCount,
                PackedCoastalWaterOffset = packedCoastalWaterOffset,
                PackedCoastalWaterCount = packedCoastalWaterCount,
                PackedCoastalLandOffset = packedCoastalLandOffset,
                PackedCoastalLandCount = packedCoastalLandCount,
                PackedTerrainSourceMeshCount = packedTerrainSourceMeshCount,
                ContourMesh = contourMesh,
                CoastlineMesh = coastlineMesh,
                ContourGeographicPoints = BuildGeographicPoints(contourSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastlineGeographicPoints = BuildGeographicPoints(coastlineSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                ContourProjectedVertices = AllocateProjectedVertices(contourSource),
                CoastlineProjectedVertices = AllocateProjectedVertices(coastlineSource),'''
text = replace_once(text, old_entry, new_entry,
    'Entry retains only packed terrain GPU/geographic/projected authority')

text = replace_once(text,
'''                LandShade = land.Shade.ToArray(),
                LandColours = new Color32[land.Vertices.Count],
                CoastalLandCorrectionElevationMeters =''',
'''                LandShade = land.Shade.ToArray(),
                CoastalLandCorrectionElevationMeters =''',
'remove legacy land colour buffer')
text = replace_once(text,
'''                CoastalLandCorrectionShade =
                    result.CoastalLandCorrectionShade == null ? null :
                    (byte[])result.CoastalLandCorrectionShade.Clone(),
                CoastalLandCorrectionColours = coastalLandCorrectionSource == null ? null :
                    new Color32[coastalLandCorrectionSource.Length],
                Resolution = result.Resolution,''',
'''                CoastalLandCorrectionShade =
                    result.CoastalLandCorrectionShade == null ? null :
                    (byte[])result.CoastalLandCorrectionShade.Clone(),
                Resolution = result.Resolution,''',
'remove legacy coastal colour buffer')

# CPU-only source helper. Unlike BuildTriangleListMesh this allocates no Unity Mesh.
marker = '''        Mesh BuildPackedTerrainMesh(string name,
            Vector3[] waterSource, List<int> waterTriangles,'''
if text.count(marker) != 1:
    raise SystemExit('packed helper insertion anchor mismatch')
helper = '''        static Vector3[] BuildTriangleSourceVertices(float[] xy)
        {
            if (xy == null || xy.Length < 6 || (xy.Length & 1) != 0 ||
                (xy.Length / 2) % 3 != 0) return null;
            int count = xy.Length / 2;
            var output = new Vector3[count];
            for (int i = 0; i < count; i++)
                output[i] = new Vector3(xy[i * 2], xy[i * 2 + 1], 0f);
            return output;
        }

'''
text = text.replace(marker, helper + marker, 1)

text = replace_once(text,
'''            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            return mesh;
        }

        Mesh BuildLineMesh''',
'''            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            operationHealthPackedTerrainBuilds++;
            return mesh;
        }

        Mesh BuildLineMesh''',
'packed build counter')

old_project = '''            if (entry.PackedTerrainMesh != null &&
                entry.PackedTerrainGeographicPoints != null &&
                entry.PackedTerrainProjectedVertices != null)
            {
                ProjectMesh(entry.PackedTerrainMesh,
                    entry.PackedTerrainGeographicPoints,
                    entry.PackedTerrainProjectedVertices, context);
            }
            else
            {
                ProjectMesh(entry.LandMesh, entry.LandGeographicPoints,
                    entry.LandProjectedVertices, context);
                ProjectMesh(entry.WaterMesh, entry.WaterGeographicPoints,
                    entry.WaterProjectedVertices, context);
                ProjectMesh(entry.CoastalLandCorrectionMesh,
                    entry.CoastalLandCorrectionGeographicPoints,
                    entry.CoastalLandCorrectionProjectedVertices, context);
                ProjectMesh(entry.CoastalWaterCorrectionMesh,
                    entry.CoastalWaterCorrectionGeographicPoints,
                    entry.CoastalWaterCorrectionProjectedVertices, context);
            }
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,'''
new_project = '''            ProjectMesh(entry.PackedTerrainMesh,
                entry.PackedTerrainGeographicPoints,
                entry.PackedTerrainProjectedVertices, context);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,'''
text = replace_once(text, old_project, new_project,
    'remove four-mesh exact projection fallback')

# Replace packed+fallback DrawEntry with a strict one-terrain-Mesh path. The accepted outer
# Entry order and per-Entry terrain->contour->coastline order remain unchanged.
start = text.index('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
end = text.index('        static void EnsurePackedTerrainColours(Entry entry,', start)
new_draw = '''        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || entry.PackedTerrainMesh == null) return false;
            EnsurePackedTerrainColours(entry, mode, preset, aircraftAltitudeAslMeters);
            bool rendered = false;
            if (terrainMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix);
                operationHealthDrawMeshSubmissions++;
                operationHealthPackedTerrainDraws++;
                int saved = Math.Max(0, entry.PackedTerrainSourceMeshCount - 1);
                operationHealthPackedTerrainDrawSubmissionsSaved += saved;
                operationHealthTerrainSetPassSaved += saved;
                rendered = true;
            }
            if (drawContours && entry.ContourMesh != null && contourMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);
                operationHealthDrawMeshSubmissions++;
            }
            if (entry.CoastlineMesh != null && coastlineMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);
                operationHealthDrawMeshSubmissions++;
            }
            return rendered;
        }

'''
text = text[:start] + new_draw + text[end:]

# Selection code must stop treating the now-null legacy Land/Water Mesh fields as readiness.
anchor = '''        void ResolveRenderableEntries(AERISTerrainHeightTile tile, string cacheKey,
            string styleKey, out Entry fallback, out Entry current)
        {'''
if text.count(anchor) != 1:
    raise SystemExit('ResolveRenderableEntries anchor mismatch')
text = text.replace(anchor,
'''        static bool HasRenderableTerrain(Entry entry)
        {
            return entry != null && entry.PackedTerrainMesh != null;
        }

''' + anchor, 1)
text = replace_once(text,
'''            if (entries.TryGetValue(cacheKey, out exact) && exact != null &&
                (exact.LandMesh != null || exact.WaterMesh != null)) current = exact;''',
'''            if (entries.TryGetValue(cacheKey, out exact) &&
                HasRenderableTerrain(exact)) current = exact;''',
'current packed readiness')
text = replace_once(text,
'''                if (candidate == null || candidate.LandMesh == null && candidate.WaterMesh == null ||
                    ReferenceEquals(candidate, current) ||''',
'''                if (!HasRenderableTerrain(candidate) ||
                    ReferenceEquals(candidate, current) ||''',
'fallback packed readiness')

text = replace_once(text,
'''            RecycleMesh(ref entry.PackedTerrainMesh);
            RecycleMesh(ref entry.LandMesh);
            RecycleMesh(ref entry.WaterMesh);
            RecycleMesh(ref entry.CoastalLandCorrectionMesh);
            RecycleMesh(ref entry.CoastalWaterCorrectionMesh);
            RecycleMesh(ref entry.ContourMesh);''',
'''            RecycleMesh(ref entry.PackedTerrainMesh);
            RecycleMesh(ref entry.ContourMesh);''',
'recycle only one terrain Unity Mesh')

text = replace_once(text,
'''                "; oh_terrain_pack_draw=" + operationHealthPackedTerrainDraws +
                "; oh_terrain_pack_saved=" + operationHealthPackedTerrainDrawSubmissionsSaved +
                "; oh_draw_mesh=" + operationHealthDrawMeshSubmissions +''',
'''                "; oh_terrain_single_build=" + operationHealthPackedTerrainBuilds +
                "; oh_terrain_pack_draw=" + operationHealthPackedTerrainDraws +
                "; oh_terrain_pack_saved=" + operationHealthPackedTerrainDrawSubmissionsSaved +
                "; oh_draw_mesh=" + operationHealthDrawMeshSubmissions +''',
'single-authority build telemetry')

renderer.write_text(text)

# Adapt permanent regressions to the single terrain authority.
pass3 = ROOT / 'Tools/selftest_v01800_operation_health_pass3_projection_draw_reduction.py'
p = pass3.read_text()
p = replace_once(p,
"ck(draw.count('terrainMaterial.SetPass(0)') == 2 and 'entry.PackedTerrainMesh != null' in draw,\n   'packed terrain and accepted fallback each use one terrain SetPass per Entry')",
"ck(draw.count('terrainMaterial.SetPass(0)') == 1 and 'entry.PackedTerrainMesh != null' in draw,\n   'single packed terrain authority uses one terrain SetPass per Entry')",
'Pass3 single SetPass contract')
old_order = '''# Inspect actual draw submissions, not earlier null/count references to the same fields.
order=[
 'Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)'
]
pos=[draw.find(x) for x in order]
ck(all(x>=0 for x in pos) and pos==sorted(pos),
   'Candidate8 terrain painter order remains unchanged')'''
new_order = '''pack=R[R.index('Mesh BuildPackedTerrainMesh('):R.index('Mesh BuildLineMesh(',R.index('Mesh BuildPackedTerrainMesh('))]
order=[
 'indices[index++] = waterOffset + waterTriangles[i]',
 'indices[index++] = landOffset + landTriangles[i]',
 'indices[index++] = coastalWaterOffset + i',
 'indices[index++] = coastalLandOffset + i'
]
pos=[pack.find(x) for x in order]
ck(all(x>=0 for x in pos) and pos==sorted(pos),
   'Candidate8 terrain painter order remains unchanged inside packed index stream')'''
if p.count(old_order) != 1:
    raise SystemExit('Pass3 painter-order anchor mismatch')
p = p.replace(old_order, new_order, 1)
pass3.write_text(p)

bridge = ROOT / 'Tools/selftest_v01800_operation_health_projection_motion_bridge.py'
b = bridge.read_text()
b = replace_once(b,
"ck('ProjectMesh(entry.LandMesh' in project and 'mesh.vertices = projectedVertices' in R,'exact projection/upload path remains intact')",
"ck('ProjectMesh(entry.PackedTerrainMesh' in project and 'mesh.vertices = projectedVertices' in R,'exact packed projection/upload path remains intact')",
'Projection Bridge packed exact-path contract')
bridge.write_text(b)

pass2 = ROOT / 'Tools/selftest_v01800_operation_health_pass2_persistent_geometry.py'
p2 = pass2.read_text()
p2 = replace_once(p2,
"      renderer.index('if (!exactProjectionDue)') < renderer.index('ProjectMesh(entry.LandMesh'))",
"      renderer.index('if (!exactProjectionDue)') < renderer.index('ProjectMesh(entry.PackedTerrainMesh'))",
'Pass2 packed projection contract')
pass2.write_text(p2)

packing_test = ROOT / 'Tools/selftest_v01800_operation_health_entry_terrain_mesh_packing.py'
packing_test.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
build=R[R.index('Entry BuildEntry('):R.index('static SurfacePoint Point(',R.index('Entry BuildEntry('))]
pack=R[R.index('Mesh BuildPackedTerrainMesh('):R.index('Mesh BuildLineMesh(',R.index('Mesh BuildPackedTerrainMesh('))]
project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(')]
draw=R[R.index('bool DrawEntry('):R.index('static void EnsurePackedTerrainColours',R.index('bool DrawEntry('))]
remove=R[R.index('void Remove(Entry entry)'):R.index('void FailGpuTerrain',R.index('void Remove(Entry entry)'))]
resolve=R[R.index('static bool HasRenderableTerrain('):R.index('void AddEntry(',R.index('static bool HasRenderableTerrain('))]
ck('BuildSurfaceMesh(' not in build and 'BuildTriangleListMesh(' not in build,
   'BuildEntry creates no legacy terrain Unity Mesh')
ck(build.count('BuildPackedTerrainMesh(') == 1,
   'BuildEntry creates one packed terrain Unity Mesh')
ck('LandMesh = landMesh' not in build and 'WaterMesh = waterMesh' not in build and
   'CoastalLandCorrectionMesh = coastalLandCorrectionMesh' not in build and
   'CoastalWaterCorrectionMesh = coastalWaterCorrectionMesh' not in build,
   'legacy terrain Mesh fields are never populated')
order=[
 'indices[index++] = waterOffset + waterTriangles[i]',
 'indices[index++] = landOffset + landTriangles[i]',
 'indices[index++] = coastalWaterOffset + i',
 'indices[index++] = coastalLandOffset + i'
]
pos=[pack.find(x) for x in order]
ck(all(x>=0 for x in pos) and pos==sorted(pos),
   'packed primitive stream preserves Candidate8 painter order')
ck('ProjectMesh(entry.PackedTerrainMesh' in project and
   'ProjectMesh(entry.LandMesh' not in project and 'ProjectMesh(entry.WaterMesh' not in project,
   'exact projection uploads one terrain vertex buffer only')
ck(draw.count('terrainMaterial.SetPass(0)') == 1 and
   draw.count('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') == 1,
   'steady Entry terrain path is exactly one SetPass and one DrawMeshNow')
ck(draw.index('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') <
   draw.index('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)') <
   draw.index('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)'),
   'Entry terrain-contour-coastline occlusion order is unchanged')
ck('RecycleMesh(ref entry.PackedTerrainMesh)' in remove and
   'RecycleMesh(ref entry.LandMesh)' not in remove and
   'RecycleMesh(ref entry.WaterMesh)' not in remove,
   'Entry lifecycle owns exactly one terrain Unity Mesh')
ck('HasRenderableTerrain(exact)' in resolve and '!HasRenderableTerrain(candidate)' in resolve,
   'current/fallback selection uses packed terrain authority')
ck('operationHealthPackedTerrainBuilds++' in pack and 'oh_terrain_single_build=' in R,
   'runtime directly exposes terrain-Mesh build churn')
ck('oh_terrain_pack_draw=' in R and 'oh_terrain_pack_saved=' in R and 'oh_draw_mesh=' in R,
   'runtime exposes draw savings and remaining submissions')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'visual RenderTexture authority unchanged')
ck('MaximumContourLevelsPerTile = 96' in (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(),
   'Candidate11 contour authority unchanged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz authority unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Single-Authority Terrain Pack] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

prebuild = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
pb = prebuild.read_text()
pb = pb.replace("('Operation Health Entry Terrain Mesh Packing','selftest_v01800_operation_health_entry_terrain_mesh_packing.py')",
                "('Operation Health Single-Authority Terrain Pack','selftest_v01800_operation_health_entry_terrain_mesh_packing.py')")
prebuild.write_text(pb)

print('[AERIS23 Single-Authority Terrain Pack] successor applied')
print('Single terrain GPU authority: PackedTerrainMesh only')
print('Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py')
print('Then: git diff --check')
