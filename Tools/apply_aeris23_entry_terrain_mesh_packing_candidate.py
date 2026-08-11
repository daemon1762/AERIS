#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text = renderer.read_text()

if 'oh_terrain_pack_draw=' in text and 'BuildPackedTerrainMesh(' in text:
    print('[AERIS23 Entry Terrain Mesh Packing] already applied')
    raise SystemExit(0)


def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old, new, 1)

# Candidate fields. Keep the accepted four source meshes as a fallback authority during
# runtime evaluation; packed terrain owns draw/projection/colour only when successfully built.
text = replace_once(text,
'''            internal Mesh LandMesh;
            internal Mesh WaterMesh;
            // Candidate8 sparse coastal correction overlays.''',
'''            internal Mesh LandMesh;
            internal Mesh WaterMesh;
            // AERIS23 Entry-Preserving Terrain Mesh Packing. One packed triangle mesh keeps
            // the exact Candidate8 primitive order (water -> land -> coastal water -> coastal
            // land) inside this Entry while reducing four DrawMeshNow submissions to one.
            // The accepted source meshes remain resident as a candidate fallback until runtime
            // acceptance proves the packed path visually identical.
            internal Mesh PackedTerrainMesh;
            internal GeographicUnitPoint[] PackedTerrainGeographicPoints;
            internal Vector3[] PackedTerrainProjectedVertices;
            internal Color32[] PackedTerrainColours;
            internal int PackedWaterOffset;
            internal int PackedWaterCount;
            internal int PackedLandOffset;
            internal int PackedLandCount;
            internal int PackedCoastalWaterOffset;
            internal int PackedCoastalWaterCount;
            internal int PackedCoastalLandOffset;
            internal int PackedCoastalLandCount;
            internal int PackedTerrainSourceMeshCount;
            // Candidate8 sparse coastal correction overlays.''',
'packed terrain Entry fields')

text = replace_once(text,
'''        long operationHealthBoundsSkips;
        long operationHealthTerrainSetPassSaved;
        // Cadence Hotfix 1:''',
'''        long operationHealthBoundsSkips;
        long operationHealthTerrainSetPassSaved;
        long operationHealthPackedTerrainDraws;
        long operationHealthPackedTerrainDrawSubmissionsSaved;
        long operationHealthDrawMeshSubmissions;
        // Cadence Hotfix 1:''',
'packed terrain telemetry fields')

# Build packed terrain after the four accepted source meshes are available.
text = replace_once(text,
'''            Mesh coastalWaterCorrectionMesh = BuildTriangleListMesh(
                "AERIS_TERRAIN_COAST_WATER_FIX_" + result.Key.FileStem,
                result.CoastalWaterCorrectionVertices, true,
                out coastalWaterCorrectionSource);
            Mesh contourMesh = BuildLineMesh("AERIS_TERRAIN_CONTOUR_" +''',
'''            Mesh coastalWaterCorrectionMesh = BuildTriangleListMesh(
                "AERIS_TERRAIN_COAST_WATER_FIX_" + result.Key.FileStem,
                result.CoastalWaterCorrectionVertices, true,
                out coastalWaterCorrectionSource);
            Vector3[] packedTerrainSource;
            Color32[] packedTerrainColours;
            int packedWaterOffset, packedWaterCount;
            int packedLandOffset, packedLandCount;
            int packedCoastalWaterOffset, packedCoastalWaterCount;
            int packedCoastalLandOffset, packedCoastalLandCount;
            int packedTerrainSourceMeshCount, packedTerrainIndexCount;
            Mesh packedTerrainMesh = BuildPackedTerrainMesh(
                "AERIS_TERRAIN_PACKED_" + result.Key.FileStem,
                waterSource, water.Triangles, landSource, land.Triangles,
                coastalWaterCorrectionSource, coastalLandCorrectionSource,
                out packedTerrainSource, out packedTerrainColours,
                out packedWaterOffset, out packedWaterCount,
                out packedLandOffset, out packedLandCount,
                out packedCoastalWaterOffset, out packedCoastalWaterCount,
                out packedCoastalLandOffset, out packedCoastalLandCount,
                out packedTerrainSourceMeshCount, out packedTerrainIndexCount);
            Mesh contourMesh = BuildLineMesh("AERIS_TERRAIN_CONTOUR_" +''',
'BuildEntry packed terrain construction')

# Account the candidate duplicate packed authority conservatively during evaluation.
text = replace_once(text,
'''            long bytes = result.Valid.Length + projectedVertexBytes +
                land.Vertices.Count * (3L * 4L + 4L + 4L) +''',
'''            long bytes = result.Valid.Length + projectedVertexBytes +
                (packedTerrainSource == null ? 0L :
                    packedTerrainSource.LongLength * (3L * 8L + 3L * 4L + 3L * 4L + 4L) +
                    packedTerrainIndexCount * 4L) +
                land.Vertices.Count * (3L * 4L + 4L + 4L) +''',
'packed terrain byte accounting')

text = replace_once(text,
'''                LandMesh = landMesh,
                WaterMesh = waterMesh,
                CoastalLandCorrectionMesh = coastalLandCorrectionMesh,''',
'''                LandMesh = landMesh,
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
                CoastalLandCorrectionMesh = coastalLandCorrectionMesh,''',
'BuildEntry packed terrain Entry assignment')

# Insert packed mesh builder before line-mesh construction.
marker = '''        Mesh BuildLineMesh(string name, float[] segments, Color32 colour,
            out Vector3[] sourceVertices)
        {'''
if text.count(marker) != 1:
    raise SystemExit('BuildLineMesh insertion anchor mismatch')
helper = r'''        Mesh BuildPackedTerrainMesh(string name,
            Vector3[] waterSource, List<int> waterTriangles,
            Vector3[] landSource, List<int> landTriangles,
            Vector3[] coastalWaterSource, Vector3[] coastalLandSource,
            out Vector3[] packedSource, out Color32[] packedColours,
            out int waterOffset, out int waterCount,
            out int landOffset, out int landCount,
            out int coastalWaterOffset, out int coastalWaterCount,
            out int coastalLandOffset, out int coastalLandCount,
            out int sourceMeshCount, out int packedIndexCount)
        {
            waterCount = waterSource == null ? 0 : waterSource.Length;
            landCount = landSource == null ? 0 : landSource.Length;
            coastalWaterCount = coastalWaterSource == null ? 0 : coastalWaterSource.Length;
            coastalLandCount = coastalLandSource == null ? 0 : coastalLandSource.Length;
            waterOffset = 0;
            landOffset = waterOffset + waterCount;
            coastalWaterOffset = landOffset + landCount;
            coastalLandOffset = coastalWaterOffset + coastalWaterCount;
            int vertexCount = coastalLandOffset + coastalLandCount;
            sourceMeshCount = (waterCount > 0 ? 1 : 0) + (landCount > 0 ? 1 : 0) +
                (coastalWaterCount > 0 ? 1 : 0) + (coastalLandCount > 0 ? 1 : 0);
            int waterIndexCount = waterTriangles == null ? 0 : waterTriangles.Count;
            int landIndexCount = landTriangles == null ? 0 : landTriangles.Count;
            packedIndexCount = waterIndexCount + landIndexCount +
                coastalWaterCount + coastalLandCount;
            packedSource = null;
            packedColours = null;
            if (vertexCount < 3 || packedIndexCount < 3 || sourceMeshCount <= 0)
                return null;

            packedSource = new Vector3[vertexCount];
            packedColours = new Color32[vertexCount];
            int[] indices = new int[packedIndexCount];
            Color32 waterColour = ResolveWaterColour(AERISTerrainColourPreset.Standard);
            Color32 landColour = new Color32(255, 255, 255, 255);

            if (waterCount > 0) Array.Copy(waterSource, 0, packedSource, waterOffset, waterCount);
            if (landCount > 0) Array.Copy(landSource, 0, packedSource, landOffset, landCount);
            if (coastalWaterCount > 0)
                Array.Copy(coastalWaterSource, 0, packedSource,
                    coastalWaterOffset, coastalWaterCount);
            if (coastalLandCount > 0)
                Array.Copy(coastalLandSource, 0, packedSource,
                    coastalLandOffset, coastalLandCount);
            for (int i = 0; i < waterCount; i++) packedColours[waterOffset + i] = waterColour;
            for (int i = 0; i < landCount; i++) packedColours[landOffset + i] = landColour;
            for (int i = 0; i < coastalWaterCount; i++)
                packedColours[coastalWaterOffset + i] = waterColour;
            for (int i = 0; i < coastalLandCount; i++)
                packedColours[coastalLandOffset + i] = landColour;

            int index = 0;
            if (waterTriangles != null)
                for (int i = 0; i < waterTriangles.Count; i++)
                    indices[index++] = waterOffset + waterTriangles[i];
            if (landTriangles != null)
                for (int i = 0; i < landTriangles.Count; i++)
                    indices[index++] = landOffset + landTriangles[i];
            for (int i = 0; i < coastalWaterCount; i++)
                indices[index++] = coastalWaterOffset + i;
            for (int i = 0; i < coastalLandCount; i++)
                indices[index++] = coastalLandOffset + i;

            Mesh mesh = AcquireMesh(name, vertexCount);
            mesh.vertices = packedSource;
            mesh.colors32 = packedColours;
            // Primitive order intentionally matches the accepted four draw calls:
            // base water, base land, sparse coastal water, sparse coastal land.
            mesh.triangles = indices;
            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            return mesh;
        }

'''
text = text.replace(marker, helper + marker, 1)

# Exact projection uploads one packed terrain vertex buffer instead of four when available.
old_project = '''            ProjectMesh(entry.LandMesh, entry.LandGeographicPoints,
                entry.LandProjectedVertices, context);
            ProjectMesh(entry.WaterMesh, entry.WaterGeographicPoints,
                entry.WaterProjectedVertices, context);
            ProjectMesh(entry.CoastalLandCorrectionMesh,
                entry.CoastalLandCorrectionGeographicPoints,
                entry.CoastalLandCorrectionProjectedVertices, context);
            ProjectMesh(entry.CoastalWaterCorrectionMesh,
                entry.CoastalWaterCorrectionGeographicPoints,
                entry.CoastalWaterCorrectionProjectedVertices, context);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,'''
new_project = '''            if (entry.PackedTerrainMesh != null &&
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
text = replace_once(text, old_project, new_project, 'packed exact projection path')

# Replace DrawEntry with an entry-order-preserving packed fast path plus the accepted fallback.
start = text.index('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
end = text.index('        void EnsureWaterColour(Entry entry,', start)
replacement = r'''        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || (entry.PackedTerrainMesh == null &&
                entry.LandMesh == null && entry.WaterMesh == null)) return false;
            bool rendered = false;
            if (entry.PackedTerrainMesh != null)
            {
                EnsurePackedTerrainColours(entry, mode, preset, aircraftAltitudeAslMeters);
                if (terrainMaterial.SetPass(0))
                {
                    Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix);
                    operationHealthDrawMeshSubmissions++;
                    operationHealthPackedTerrainDraws++;
                    int saved = Math.Max(0, entry.PackedTerrainSourceMeshCount - 1);
                    operationHealthPackedTerrainDrawSubmissionsSaved += saved;
                    // Preserve the legacy metric meaning: redundant terrain SetPass calls
                    // avoided relative to one SetPass per source terrain mesh.
                    operationHealthTerrainSetPassSaved += saved;
                    rendered = true;
                }
            }
            else
            {
                EnsureLandColours(entry, mode, preset, aircraftAltitudeAslMeters);
                EnsureWaterColour(entry, preset);
                int terrainMeshCount = (entry.WaterMesh == null ? 0 : 1) +
                    (entry.LandMesh == null ? 0 : 1) +
                    (entry.CoastalWaterCorrectionMesh == null ? 0 : 1) +
                    (entry.CoastalLandCorrectionMesh == null ? 0 : 1);
                if (terrainMeshCount > 0 && terrainMaterial.SetPass(0))
                {
                    // Candidate8 fallback painter order remains unchanged.
                    if (entry.WaterMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                    }
                    if (entry.LandMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                    }
                    if (entry.CoastalWaterCorrectionMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                    }
                    if (entry.CoastalLandCorrectionMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                    }
                    rendered = true;
                    operationHealthTerrainSetPassSaved += Math.Max(0, terrainMeshCount - 1);
                }
            }
            // Critical safety invariant: overlays remain inside this Entry. The outer tile
            // loop still completes Entry A terrain/contour/coast before Entry B begins.
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

        static void EnsurePackedTerrainColours(Entry entry,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || entry.PackedTerrainMesh == null ||
                entry.PackedTerrainColours == null) return;
            int altitudeBucket = mode == AERISTerrainDisplayMode.Relative ?
                Mathf.RoundToInt(aircraftAltitudeAslMeters / RelativeAltitudeBucketMeters) :
                int.MinValue;
            bool waterChanged = entry.WaterColourPreset != preset;
            bool landChanged = entry.ColourMode != mode || entry.ColourPreset != preset ||
                entry.RelativeAltitudeBucket != altitudeBucket;
            if (!waterChanged && !landChanged) return;

            if (waterChanged)
            {
                Color32 waterColour = ResolveWaterColour(preset);
                int waterEnd = Math.Min(entry.PackedTerrainColours.Length,
                    entry.PackedWaterOffset + entry.PackedWaterCount);
                for (int i = Math.Max(0, entry.PackedWaterOffset); i < waterEnd; i++)
                    entry.PackedTerrainColours[i] = waterColour;
                int coastalWaterEnd = Math.Min(entry.PackedTerrainColours.Length,
                    entry.PackedCoastalWaterOffset + entry.PackedCoastalWaterCount);
                for (int i = Math.Max(0, entry.PackedCoastalWaterOffset);
                    i < coastalWaterEnd; i++) entry.PackedTerrainColours[i] = waterColour;
                entry.WaterColourPreset = preset;
            }

            if (landChanged)
            {
                float quantizedAltitude = mode == AERISTerrainDisplayMode.Relative ?
                    altitudeBucket * RelativeAltitudeBucketMeters : aircraftAltitudeAslMeters;
                int landCount = Math.Min(entry.PackedLandCount,
                    entry.LandElevationMeters == null ? 0 : entry.LandElevationMeters.Length);
                landCount = Math.Min(landCount,
                    entry.LandShade == null ? 0 : entry.LandShade.Length);
                for (int i = 0; i < landCount; i++)
                {
                    Color32 baseColour = ResolveLandColour(mode, preset,
                        entry.LandElevationMeters[i], quantizedAltitude);
                    int target = entry.PackedLandOffset + i;
                    if (target >= 0 && target < entry.PackedTerrainColours.Length)
                        entry.PackedTerrainColours[target] =
                            ApplyShade(baseColour, entry.LandShade[i], mode);
                }
                int coastalLandCount = Math.Min(entry.PackedCoastalLandCount,
                    entry.CoastalLandCorrectionElevationMeters == null ? 0 :
                    entry.CoastalLandCorrectionElevationMeters.Length);
                for (int i = 0; i < coastalLandCount; i++)
                {
                    Color32 baseColour = ResolveLandColour(mode, preset,
                        entry.CoastalLandCorrectionElevationMeters[i], quantizedAltitude);
                    byte shade = entry.CoastalLandCorrectionShade != null &&
                        i < entry.CoastalLandCorrectionShade.Length ?
                        entry.CoastalLandCorrectionShade[i] : (byte)255;
                    int target = entry.PackedCoastalLandOffset + i;
                    if (target >= 0 && target < entry.PackedTerrainColours.Length)
                        entry.PackedTerrainColours[target] =
                            ApplyShade(baseColour, shade, mode);
                }
                entry.ColourMode = mode;
                entry.ColourPreset = preset;
                entry.RelativeAltitudeBucket = altitudeBucket;
            }
            // Water and land dirty states are merged into one packed colour upload.
            entry.PackedTerrainMesh.colors32 = entry.PackedTerrainColours;
        }

'''
text = text[:start] + replacement + text[end:]

# Recycle candidate packed mesh with the Entry lifecycle.
text = replace_once(text,
'''            RecycleMesh(ref entry.LandMesh);
            RecycleMesh(ref entry.WaterMesh);
            RecycleMesh(ref entry.CoastalLandCorrectionMesh);''',
'''            RecycleMesh(ref entry.PackedTerrainMesh);
            RecycleMesh(ref entry.LandMesh);
            RecycleMesh(ref entry.WaterMesh);
            RecycleMesh(ref entry.CoastalLandCorrectionMesh);''',
'packed terrain recycle')

# Runtime observability.
text = replace_once(text,
'''                "; oh_bounds_skip=" + operationHealthBoundsSkips +
                "; oh_setpass_saved=" + operationHealthTerrainSetPassSaved +
                "; oh_cadence_defer=" + operationHealthCadenceDeferrals +''',
'''                "; oh_bounds_skip=" + operationHealthBoundsSkips +
                "; oh_setpass_saved=" + operationHealthTerrainSetPassSaved +
                "; oh_terrain_pack_draw=" + operationHealthPackedTerrainDraws +
                "; oh_terrain_pack_saved=" + operationHealthPackedTerrainDrawSubmissionsSaved +
                "; oh_draw_mesh=" + operationHealthDrawMeshSubmissions +
                "; oh_cadence_defer=" + operationHealthCadenceDeferrals +''',
'packed terrain telemetry log')

renderer.write_text(text)

# Existing Pass3 test: packed and fallback terrain branches each own exactly one SetPass.
pass3 = ROOT / 'Tools/selftest_v01800_operation_health_pass3_projection_draw_reduction.py'
p = pass3.read_text()
old = "ck(draw.count('terrainMaterial.SetPass(0)') == 1,\n   'terrain meshes share one material SetPass per entry')"
new = "ck(draw.count('terrainMaterial.SetPass(0)') == 2 and 'entry.PackedTerrainMesh != null' in draw,\n   'packed terrain and accepted fallback each use one terrain SetPass per Entry')"
if p.count(old) != 1:
    raise SystemExit('Pass3 SetPass assertion anchor mismatch')
p = p.replace(old, new, 1)
pass3.write_text(p)

# Dedicated regression: exact Entry ordering is the non-negotiable difference from rejected PR22.
packing_test = ROOT / 'Tools/selftest_v01800_operation_health_entry_terrain_mesh_packing.py'
packing_test.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
build=R[R.index('Mesh BuildPackedTerrainMesh('):R.index('Mesh BuildLineMesh(',R.index('Mesh BuildPackedTerrainMesh('))]
draw=R[R.index('bool DrawEntry('):R.index('void EnsureWaterColour',R.index('bool DrawEntry('))]
render=R[R.index('bool RenderBackBuffer('):R.index('float MeasureFoundationGpuReadiness')]
project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(')]
ck('PackedTerrainMesh' in R and 'PackedTerrainGeographicPoints' in R and
   'PackedTerrainProjectedVertices' in R,'packed terrain retains mesh/geographic/projected authority')
ck('waterOffset = 0' in build and 'landOffset = waterOffset + waterCount' in build and
   'coastalWaterOffset = landOffset + landCount' in build and
   'coastalLandOffset = coastalWaterOffset + coastalWaterCount' in build,
   'packed vertex blocks preserve Candidate8 water-land-coastal-water-coastal-land order')
wi=build.index('waterOffset + waterTriangles[i]')
li=build.index('landOffset + landTriangles[i]')
cwi=build.index('indices[index++] = coastalWaterOffset + i')
cli=build.index('indices[index++] = coastalLandOffset + i')
ck(wi < li < cwi < cli,'packed primitive index order exactly preserves Candidate8 painter order')
ck('AcquireMesh(name, vertexCount)' in build,'packed mesh inherits 32-bit index safety from AcquireMesh')
ck('ProjectMesh(entry.PackedTerrainMesh' in project and
   project.index('ProjectMesh(entry.PackedTerrainMesh') < project.index('else\n            {'),
   'exact projection prefers one packed terrain vertex upload')
ck('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)' in draw,
   'packed terrain reduces source terrain surfaces to one DrawMeshNow')
ck(draw.index('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') <
   draw.index('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)') <
   draw.index('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)'),
   'each Entry still completes terrain then contour then coastline')
ck("bool entryRendered = DrawEntry(drawEntry, entryMapMatrix" in render and
   'DrawLayerBatches' not in render,'outer BACK loop preserves exact per-Entry submission order')
ck('EnsurePackedTerrainColours(entry, mode, preset' in draw and
   'entry.PackedTerrainMesh.colors32 = entry.PackedTerrainColours' in R,
   'packed REL/TOPO/water colours share one dirty-guarded colour buffer upload')
ck('RecycleMesh(ref entry.PackedTerrainMesh)' in R,'packed mesh follows existing Entry recycle lifecycle')
ck('oh_terrain_pack_draw=' in R and 'oh_terrain_pack_saved=' in R and 'oh_draw_mesh=' in R,
   'runtime exposes packed draw savings and remaining DrawMeshNow submissions')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'visual RenderTexture authority unchanged')
ck('MaximumContourLevelsPerTile = 96' in (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(),
   'Candidate11 contour authority unchanged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz authority unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Entry Terrain Mesh Packing] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

prebuild = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
pb = prebuild.read_text()
marker = " ('Operation Health Projection Motion Bridge','selftest_v01800_operation_health_projection_motion_bridge.py'),"
addition = " ('Operation Health Entry Terrain Mesh Packing','selftest_v01800_operation_health_entry_terrain_mesh_packing.py'),"
if 'selftest_v01800_operation_health_entry_terrain_mesh_packing.py' not in pb:
    if marker not in pb:
        raise SystemExit('prebuild Projection Motion Bridge marker absent')
    pb = pb.replace(marker, marker + '\n' + addition, 1)
prebuild.write_text(pb)

print('[AERIS23 Entry Terrain Mesh Packing] patch applied')
print('Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py')
print('Then: git diff --check')
