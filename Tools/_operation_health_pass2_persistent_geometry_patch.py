#!/usr/bin/env python3
from pathlib import Path

root = Path(__file__).resolve().parents[1]

# -----------------------------------------------------------------------------
# Rasterizer: remove hot-path transient allocations without changing output.
# -----------------------------------------------------------------------------
rp = root / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
r = rp.read_text(encoding='utf-8')

old = '''        readonly Dictionary<string, PendingState> pending = new Dictionary<string, PendingState>(StringComparer.Ordinal);\n        readonly Queue<AERISTerrainGpuTileRasterResult> completed = new Queue<AERISTerrainGpuTileRasterResult>();\n        bool disposed;'''
new = '''        readonly Dictionary<string, PendingState> pending = new Dictionary<string, PendingState>(StringComparer.Ordinal);\n        readonly Queue<AERISTerrainGpuTileRasterResult> completed = new Queue<AERISTerrainGpuTileRasterResult>();\n        // Operation Health Pass 2: lifecycle cancellation is uncommon but must not create\n        // a fresh scheduler-key List every time the ND viewport is reset.\n        readonly List<string> cancelSchedulerKeysScratch = new List<string>(32);\n        bool disposed;'''
if old not in r: raise SystemExit('rasterizer field anchor not found')
r = r.replace(old, new, 1)

old = '''        internal void CancelAll()\n        {\n            var schedulerKeys = new List<string>();\n            lock (gate)\n            {\n                foreach (PendingState state in pending.Values) if (state != null && !string.IsNullOrEmpty(state.SchedulerKey)) schedulerKeys.Add(state.SchedulerKey);\n                pending.Clear(); completed.Clear();\n            }\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime == null) return;\n            for (int i = 0; i < schedulerKeys.Count; i++) runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute, schedulerKeys[i]);\n        }'''
new = '''        internal void CancelAll()\n        {\n            cancelSchedulerKeysScratch.Clear();\n            lock (gate)\n            {\n                foreach (PendingState state in pending.Values)\n                    if (state != null && !string.IsNullOrEmpty(state.SchedulerKey))\n                        cancelSchedulerKeysScratch.Add(state.SchedulerKey);\n                pending.Clear(); completed.Clear();\n            }\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)\n                for (int i = 0; i < cancelSchedulerKeysScratch.Count; i++)\n                    runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,\n                        cancelSchedulerKeysScratch[i]);\n            cancelSchedulerKeysScratch.Clear();\n        }'''
if old not in r: raise SystemExit('CancelAll anchor not found')
r = r.replace(old, new, 1)

old = '''            var triangles = new List<int>((resolution - 1) * (resolution - 1) * 6);\n            for (int row = 0; row < resolution - 1; row++) for (int column = 0; column < resolution - 1; column++)\n            {\n                int a = row * resolution + column, b = a + 1, c = a + resolution, d = c + 1;\n                if (valid[a] != 0 && valid[b] != 0 && valid[c] != 0) { triangles.Add(a); triangles.Add(c); triangles.Add(b); }\n                if (valid[b] != 0 && valid[c] != 0 && valid[d] != 0) { triangles.Add(b); triangles.Add(c); triangles.Add(d); }\n            }'''
new = '''            // Operation Health Pass 2: topology is immutable once the worker result is\n            // published. Count first and allocate the exact index payload once instead of\n            // growing a List<int> and then allocating a second ToArray() copy.\n            int[] triangles = BuildTriangleIndices(valid, resolution);'''
if old not in r: raise SystemExit('triangle list anchor not found')
r = r.replace(old, new, 1)

old = '''                VertexX = x, VertexY = y, ElevationMeters = elevationMeters, Water = water, Valid = valid, Shade = shade, Triangles = triangles.ToArray(), ContourSegments = contours,'''
new = '''                VertexX = x, VertexY = y, ElevationMeters = elevationMeters, Water = water, Valid = valid, Shade = shade, Triangles = triangles, ContourSegments = contours,'''
if old not in r: raise SystemExit('triangle result anchor not found')
r = r.replace(old, new, 1)

anchor = '''        struct CorrectionPoint { internal float X; internal float Y; internal byte ClassFlag; internal float Elevation; }\n'''
helper = '''        static int[] BuildTriangleIndices(byte[] valid, int resolution)\n        {\n            if (valid == null || resolution < 2) return new int[0];\n            int indexCount = 0;\n            for (int row = 0; row < resolution - 1; row++)\n                for (int column = 0; column < resolution - 1; column++)\n                {\n                    int a = row * resolution + column;\n                    int b = a + 1;\n                    int c = a + resolution;\n                    int d = c + 1;\n                    if (valid[a] != 0 && valid[b] != 0 && valid[c] != 0)\n                        indexCount += 3;\n                    if (valid[b] != 0 && valid[c] != 0 && valid[d] != 0)\n                        indexCount += 3;\n                }\n            if (indexCount <= 0) return new int[0];\n            var triangles = new int[indexCount];\n            int write = 0;\n            for (int row = 0; row < resolution - 1; row++)\n                for (int column = 0; column < resolution - 1; column++)\n                {\n                    int a = row * resolution + column;\n                    int b = a + 1;\n                    int c = a + resolution;\n                    int d = c + 1;\n                    if (valid[a] != 0 && valid[b] != 0 && valid[c] != 0)\n                    {\n                        triangles[write++] = a;\n                        triangles[write++] = c;\n                        triangles[write++] = b;\n                    }\n                    if (valid[b] != 0 && valid[c] != 0 && valid[d] != 0)\n                    {\n                        triangles[write++] = b;\n                        triangles[write++] = c;\n                        triangles[write++] = d;\n                    }\n                }\n            return triangles;\n        }\n\n'''+anchor
if anchor not in r: raise SystemExit('CorrectionPoint anchor not found')
r = r.replace(anchor, helper, 1)

old = '''            var land = new List<float>(4096); var water = new List<float>(4096); var landHeights = new List<float>(2048); var landShades = new List<byte>(2048);\n            float baseCellMeters = (float)Math.Max(1.0, AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod));'''
new = '''            var land = new List<float>(4096); var water = new List<float>(4096); var landHeights = new List<float>(2048); var landShades = new List<byte>(2048);\n            // One pair of clip buffers serves every sub-triangle in this worker build.\n            // Candidate11 created two arrays per polygon, which produced heavy short-lived\n            // GC pressure on complex coastlines without contributing to the result.\n            var correctionInput = new CorrectionPoint[3];\n            var correctionClip = new CorrectionPoint[6];\n            float baseCellMeters = (float)Math.Max(1.0, AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod));'''
if old not in r: raise SystemExit('coastal scratch anchor not found')
r = r.replace(old, new, 1)

old = '''                    AppendCorrectionTriangle(tile, land, landHeights, landShades, water, a, c, b, shadingEnabled, baseCellMeters);\n                    AppendCorrectionTriangle(tile, land, landHeights, landShades, water, b, c, d, shadingEnabled, baseCellMeters);'''
new = '''                    AppendCorrectionTriangle(tile, land, landHeights, landShades, water,\n                        correctionInput, correctionClip, a, c, b, shadingEnabled,\n                        baseCellMeters);\n                    AppendCorrectionTriangle(tile, land, landHeights, landShades, water,\n                        correctionInput, correctionClip, b, c, d, shadingEnabled,\n                        baseCellMeters);'''
if old not in r: raise SystemExit('coastal triangle call anchor not found')
r = r.replace(old, new, 1)

old = '''        static void AppendCorrectionTriangle(AERISTerrainHeightTile tile, List<float> land, List<float> landHeights, List<byte> landShades, List<float> water, CorrectionPoint a, CorrectionPoint b, CorrectionPoint c, bool shadingEnabled, float baseCellMeters)\n        {\n            var input = new CorrectionPoint[] { a, b, c };\n            AppendCorrectionPolygon(tile, land, landHeights, landShades, input, false, shadingEnabled, baseCellMeters);\n            AppendCorrectionPolygon(tile, water, null, null, input, true, false, baseCellMeters);\n        }\n\n        static void AppendCorrectionPolygon(AERISTerrainHeightTile tile, List<float> output, List<float> elevations, List<byte> shades, CorrectionPoint[] input, bool targetWater, bool shadingEnabled, float baseCellMeters)\n        {\n            var clipped = new CorrectionPoint[6]; int count = 0;'''
new = '''        static void AppendCorrectionTriangle(AERISTerrainHeightTile tile, List<float> land,\n            List<float> landHeights, List<byte> landShades, List<float> water,\n            CorrectionPoint[] input, CorrectionPoint[] clipped, CorrectionPoint a,\n            CorrectionPoint b, CorrectionPoint c, bool shadingEnabled,\n            float baseCellMeters)\n        {\n            input[0] = a; input[1] = b; input[2] = c;\n            AppendCorrectionPolygon(tile, land, landHeights, landShades, input, clipped,\n                false, shadingEnabled, baseCellMeters);\n            AppendCorrectionPolygon(tile, water, null, null, input, clipped, true, false,\n                baseCellMeters);\n        }\n\n        static void AppendCorrectionPolygon(AERISTerrainHeightTile tile, List<float> output,\n            List<float> elevations, List<byte> shades, CorrectionPoint[] input,\n            CorrectionPoint[] clipped, bool targetWater, bool shadingEnabled,\n            float baseCellMeters)\n        {\n            int count = 0;'''
if old not in r: raise SystemExit('coastal polygon method anchor not found')
r = r.replace(old, new, 1)

rp.write_text(r, encoding='utf-8')

# -----------------------------------------------------------------------------
# Renderer: reusable builders + bounded native Mesh pool.
# -----------------------------------------------------------------------------
p = root / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
s = p.read_text(encoding='utf-8')

old = '''            internal readonly List<int> Triangles = new List<int>();\n\n            internal void AddPolygon(SurfacePoint[] points, int count)'''
new = '''            internal readonly List<int> Triangles = new List<int>();\n\n            internal void Reset()\n            {\n                Vertices.Clear();\n                Elevation.Clear();\n                Shade.Clear();\n                Triangles.Clear();\n            }\n\n            internal void AddPolygon(SurfacePoint[] points, int count)'''
if old not in s: raise SystemExit('SurfaceBuilder anchor not found')
s = s.replace(old, new, 1)

old = '''        readonly List<Entry> supersededScratch = new List<Entry>(16);\n        // Reusable exact-length presentation scratch.'''
new = '''        readonly List<Entry> supersededScratch = new List<Entry>(16);\n        // Operation Health Pass 2: BuildEntry is main-thread serialized. Keep the large\n        // List backing arrays and clipping storage alive between tile uploads instead of\n        // re-growing and collecting them for every replacement tile.\n        readonly SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();\n        readonly SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();\n        readonly SurfacePoint[] surfaceClipScratch = new SurfacePoint[6];\n        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);\n        // Recycle native Unity Mesh objects across ordinary tile eviction/supersession.\n        // Terrain OFF / viewport suspension still destroys the pool, preserving the\n        // existing resource-release contract.\n        const int MaximumPooledMeshes = 96;\n        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);\n        long operationHealthMeshPoolHits;\n        long operationHealthMeshPoolMisses;\n        long operationHealthMeshPoolRecycles;\n        long operationHealthMeshPoolDestroys;\n        long operationHealthSurfaceBuilderReuses;\n        // Reusable exact-length presentation scratch.'''
if old not in s: raise SystemExit('renderer scratch anchor not found')
s = s.replace(old, new, 1)

old = '''                \"; oh_prepared_entry_uses=\" + operationHealthPreparedEntryUses +\n                \"; cpu_terrain_draw=0.\");'''
new = '''                \"; oh_prepared_entry_uses=\" + operationHealthPreparedEntryUses +\n                \"; oh_mesh_pool=\" + meshPool.Count +\n                \"; oh_mesh_pool_hit=\" + operationHealthMeshPoolHits +\n                \"; oh_mesh_pool_miss=\" + operationHealthMeshPoolMisses +\n                \"; oh_mesh_recycle=\" + operationHealthMeshPoolRecycles +\n                \"; oh_mesh_destroy=\" + operationHealthMeshPoolDestroys +\n                \"; oh_surface_builder_reuse=\" + operationHealthSurfaceBuilderReuses +\n                \"; cpu_terrain_draw=0.\");'''
if old not in s: raise SystemExit('telemetry anchor not found')
s = s.replace(old, new, 1)

old = '''        static Entry BuildEntry(string cacheKey,\n            AERISTerrainRenderReadyHeightField result)\n        {\n            var land = new SurfaceBuilder();\n            var water = new SurfaceBuilder();\n            var clipped = new SurfacePoint[6];'''
new = '''        Entry BuildEntry(string cacheKey,\n            AERISTerrainRenderReadyHeightField result)\n        {\n            SurfaceBuilder land = landSurfaceScratch;\n            SurfaceBuilder water = waterSurfaceScratch;\n            land.Reset();\n            water.Reset();\n            SurfacePoint[] clipped = surfaceClipScratch;\n            operationHealthSurfaceBuilderReuses++;'''
if old not in s: raise SystemExit('BuildEntry scratch anchor not found')
s = s.replace(old, new, 1)

old = '''        static void AppendClippedTriangle(SurfaceBuilder builder,\n            SurfacePoint[] output, SurfacePoint a, SurfacePoint b, SurfacePoint c,\n            bool targetWater)\n        {\n            SurfacePoint[] input = { a, b, c };\n            int count = 0;\n            for (int i = 0; i < 3; i++)\n            {\n                SurfacePoint current = input[i];\n                SurfacePoint next = input[(i + 1) % 3];\n                bool currentInside = current.Water == targetWater;\n                bool nextInside = next.Water == targetWater;\n                if (currentInside) output[count++] = current;\n                if (currentInside != nextInside)\n                    output[count++] = CoastBoundaryPoint(current, next, targetWater);\n            }\n            builder.AddPolygon(output, count);\n        }'''
new = '''        static void AppendClippedTriangle(SurfaceBuilder builder,\n            SurfacePoint[] output, SurfacePoint a, SurfacePoint b, SurfacePoint c,\n            bool targetWater)\n        {\n            int count = 0;\n            AppendClippedEdge(output, ref count, a, b, targetWater);\n            AppendClippedEdge(output, ref count, b, c, targetWater);\n            AppendClippedEdge(output, ref count, c, a, targetWater);\n            builder.AddPolygon(output, count);\n        }\n\n        static void AppendClippedEdge(SurfacePoint[] output, ref int count,\n            SurfacePoint current, SurfacePoint next, bool targetWater)\n        {\n            bool currentInside = current.Water == targetWater;\n            bool nextInside = next.Water == targetWater;\n            if (currentInside) output[count++] = current;\n            if (currentInside != nextInside)\n                output[count++] = CoastBoundaryPoint(current, next, targetWater);\n        }'''
if old not in s: raise SystemExit('AppendClippedTriangle anchor not found')
s = s.replace(old, new, 1)

for sig in [
    '        static Mesh BuildSurfaceMesh(string name, SurfaceBuilder builder, bool water,',
    '        static Mesh BuildTriangleListMesh(string name, float[] xy,',
    '        static Mesh BuildLineMesh(string name, float[] segments, Color32 colour,']:
    if sig not in s: raise SystemExit('mesh builder signature missing: '+sig)
    s = s.replace(sig, sig.replace('static Mesh', 'Mesh'), 1)

s = s.replace('''            var mesh = new Mesh();\n            mesh.name = name;\n            mesh.hideFlags = HideFlags.HideAndDontSave;\n            if (builder.Vertices.Count > 65535)\n                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;\n            mesh.MarkDynamic();''', '''            Mesh mesh = AcquireMesh(name, builder.Vertices.Count);''', 1)
s = s.replace('''            var mesh = new Mesh();\n            mesh.name = name;\n            mesh.hideFlags = HideFlags.HideAndDontSave;\n            if (vertexCount > 65535)\n                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;\n            mesh.MarkDynamic();''', '''            Mesh mesh = AcquireMesh(name, vertexCount);''', 1)
s = s.replace('''            var mesh = new Mesh();\n            mesh.name = name;\n            mesh.hideFlags = HideFlags.HideAndDontSave;\n            if (vertexCount > 65535)\n                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;\n            mesh.MarkDynamic();''', '''            Mesh mesh = AcquireMesh(name, vertexCount);''', 1)

anchor = '''        static Vector3[] AllocateProjectedVertices(Vector3[] sourceVertices)\n        {\n            return sourceVertices == null ? null : new Vector3[sourceVertices.Length];\n        }\n'''
methods = '''        Mesh AcquireMesh(string name, int vertexCount)\n        {\n            Mesh mesh = null;\n            while (meshPool.Count > 0 && mesh == null)\n                mesh = meshPool.Dequeue();\n            if (mesh == null)\n            {\n                mesh = new Mesh();\n                operationHealthMeshPoolMisses++;\n            }\n            else\n            {\n                mesh.Clear();\n                operationHealthMeshPoolHits++;\n            }\n            mesh.name = name ?? \"AERIS_TERRAIN_MESH\";\n            mesh.hideFlags = HideFlags.HideAndDontSave;\n            mesh.indexFormat = vertexCount > 65535 ?\n                UnityEngine.Rendering.IndexFormat.UInt32 :\n                UnityEngine.Rendering.IndexFormat.UInt16;\n            mesh.MarkDynamic();\n            return mesh;\n        }\n\n        void RecycleMesh(ref Mesh mesh)\n        {\n            if (mesh == null) return;\n            Mesh value = mesh;\n            mesh = null;\n            if (!disposed && meshPool.Count < MaximumPooledMeshes)\n            {\n                try\n                {\n                    value.Clear();\n                    value.name = \"AERIS_TERRAIN_MESH_POOL\";\n                    meshPool.Enqueue(value);\n                    operationHealthMeshPoolRecycles++;\n                    return;\n                }\n                catch { }\n            }\n            DestroyUnityObject(value);\n            operationHealthMeshPoolDestroys++;\n        }\n\n        void DestroyMeshPool()\n        {\n            while (meshPool.Count > 0)\n            {\n                Mesh mesh = meshPool.Dequeue();\n                DestroyUnityObject(mesh);\n                operationHealthMeshPoolDestroys++;\n            }\n        }\n\n'''+anchor
if anchor not in s: raise SystemExit('AllocateProjectedVertices anchor not found')
s = s.replace(anchor, methods, 1)

old = '''                Valid = (byte[])result.Valid.Clone(),\n                CoverageFraction = TriangleCoverage(result),'''
new = '''                Valid = (byte[])result.Valid.Clone(),\n                // Water meshes are created with the frozen Standard water colour. Mark that\n                // fact so the first Standard draw does not allocate and upload an identical\n                // colour array. Non-Standard presets still update through the existing path.\n                WaterColourPreset = AERISTerrainColourPreset.Standard,\n                CoverageFraction = TriangleCoverage(result),'''
if old not in s: raise SystemExit('WaterColourPreset initialization anchor not found')
s = s.replace(old, new, 1)

old = '''            DestroyUnityObject(entry.LandMesh);\n            DestroyUnityObject(entry.WaterMesh);\n            DestroyUnityObject(entry.CoastalLandCorrectionMesh);\n            DestroyUnityObject(entry.CoastalWaterCorrectionMesh);\n            DestroyUnityObject(entry.ContourMesh);\n            DestroyUnityObject(entry.CoastlineMesh);\n            entry.LandMesh = null;\n            entry.WaterMesh = null;\n            entry.CoastalLandCorrectionMesh = null;\n            entry.CoastalWaterCorrectionMesh = null;\n            entry.ContourMesh = null;\n            entry.CoastlineMesh = null;'''
new = '''            RecycleMesh(ref entry.LandMesh);\n            RecycleMesh(ref entry.WaterMesh);\n            RecycleMesh(ref entry.CoastalLandCorrectionMesh);\n            RecycleMesh(ref entry.CoastalWaterCorrectionMesh);\n            RecycleMesh(ref entry.ContourMesh);\n            RecycleMesh(ref entry.CoastlineMesh);'''
if old not in s: raise SystemExit('Remove mesh destroy anchor not found')
s = s.replace(old, new, 1)

old = '''        void ReleaseGpuResources()\n        {\n            Entry[] snapshot = new Entry[entries.Count];\n            entries.Values.CopyTo(snapshot, 0);\n            for (int i = 0; i < snapshot.Length; i++) Remove(snapshot[i]);\n            entries.Clear();\n            entriesByTile.Clear();'''
new = '''        void ReleaseGpuResources()\n        {\n            releaseEntryScratch.Clear();\n            foreach (Entry entry in entries.Values) releaseEntryScratch.Add(entry);\n            for (int i = 0; i < releaseEntryScratch.Count; i++)\n                Remove(releaseEntryScratch[i]);\n            releaseEntryScratch.Clear();\n            entries.Clear();\n            entriesByTile.Clear();\n            // Terrain OFF/suspension means release presentation GPU resources, including\n            // the bounded recycle pool. Ordinary eviction retains the pool for reuse.\n            DestroyMeshPool();'''
if old not in s: raise SystemExit('ReleaseGpuResources anchor not found')
s = s.replace(old, new, 1)

p.write_text(s, encoding='utf-8')
print('Operation Health Pass 2 persistent geometry patch applied')
