#!/usr/bin/env python3
from pathlib import Path
p=Path('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
s=p.read_text()

def rep(old,new,marker):
    global s
    if marker in s:
        return
    if old not in s:
        raise SystemExit('anchor not found: '+marker)
    s=s.replace(old,new,1)

rep('''        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);\n        long operationHealthMeshPoolHits;''','''        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);\n        // Operation Health Pass 3: immutable identity indices and uniform-colour upload\n        // buffers are keyed by vertex count. Unity copies these arrays on assignment, so\n        // the same managed buffers can safely serve later meshes without visual coupling.\n        readonly Dictionary<int, int[]> identityIndexCache = new Dictionary<int, int[]>();\n        readonly Dictionary<int, Color32[]> uniformColourScratch = new Dictionary<int, Color32[]>();\n        static readonly Bounds NdPresentationBounds = new Bounds(\n            new Vector3(0.5f, 0.5f, 0f), new Vector3(32f, 32f, 4f));\n        long operationHealthIdentityIndexHits;\n        long operationHealthIdentityIndexMisses;\n        long operationHealthUniformColourReuses;\n        long operationHealthBoundsSkips;\n        long operationHealthTerrainSetPassSaved;\n        long operationHealthMeshPoolHits;''','identityIndexCache')

rep('''            mesh.triangles = builder.Triangles.ToArray();\n            mesh.RecalculateBounds();\n            // Colours and geographic projection are updated in flight; retain CPU access.''','''            mesh.triangles = builder.Triangles.ToArray();\n            // ND geometry is rendered in normalized presentation space. Use one conservative\n            // bound instead of rescanning every projected vertex on each map recenter.\n            mesh.bounds = NdPresentationBounds;\n            // Colours and geographic projection are updated in flight; retain CPU access.''','mesh.bounds = NdPresentationBounds;')

rep('''            sourceVertices = new Vector3[vertexCount];\n            var indices = new int[vertexCount];\n            var colours = new Color32[vertexCount];''','''            sourceVertices = new Vector3[vertexCount];\n            int[] indices = GetIdentityIndices(vertexCount);\n            var colours = new Color32[vertexCount];''','int[] indices = GetIdentityIndices(vertexCount);')
s=s.replace('''                indices[i] = i;\n                colours[i] = initial;''','''                colours[i] = initial;''',1)
rep('''            mesh.triangles = indices;\n            mesh.RecalculateBounds();\n            mesh.UploadMeshData(false);''','''            mesh.triangles = indices;\n            mesh.bounds = NdPresentationBounds;\n            mesh.UploadMeshData(false);''','mesh.triangles = indices;\n            mesh.bounds = NdPresentationBounds;')

# Line mesh has a second identity-index allocation/loop.
old='''            var vertices = new Vector3[vertexCount];\n            var indices = new int[vertexCount];\n            var colours = new Color32[vertexCount];'''
new='''            var vertices = new Vector3[vertexCount];\n            int[] indices = GetIdentityIndices(vertexCount);\n            var colours = new Color32[vertexCount];'''
if 'var vertices = new Vector3[vertexCount];\n            int[] indices = GetIdentityIndices(vertexCount);' not in s:
    if old not in s: raise SystemExit('anchor not found: line identity cache')
    s=s.replace(old,new,1)
line_start=s.index('        Mesh BuildLineMesh(')
line_end=s.index('        Mesh AcquireMesh(', line_start)
line=s[line_start:line_end]
line=line.replace('''                indices[i] = i;\n                colours[i] = colour;''','''                colours[i] = colour;''')
line=line.replace('''            mesh.SetIndices(indices, MeshTopology.Lines, 0);\n            mesh.RecalculateBounds();''','''            mesh.SetIndices(indices, MeshTopology.Lines, 0);\n            mesh.bounds = NdPresentationBounds;''')
s=s[:line_start]+line+s[line_end:]

anchor='''        Mesh AcquireMesh(string name, int vertexCount)\n        {'''
if 'int[] GetIdentityIndices(int vertexCount)' not in s:
    helper='''        int[] GetIdentityIndices(int vertexCount)\n        {\n            vertexCount = Math.Max(0, vertexCount);\n            int[] indices;\n            if (identityIndexCache.TryGetValue(vertexCount, out indices))\n            {\n                operationHealthIdentityIndexHits++;\n                return indices;\n            }\n            indices = new int[vertexCount];\n            for (int i = 0; i < vertexCount; i++) indices[i] = i;\n            identityIndexCache[vertexCount] = indices;\n            operationHealthIdentityIndexMisses++;\n            return indices;\n        }\n\n        Color32[] GetUniformColourScratch(int vertexCount, Color32 colour)\n        {\n            vertexCount = Math.Max(0, vertexCount);\n            Color32[] colours;\n            if (!uniformColourScratch.TryGetValue(vertexCount, out colours))\n            {\n                colours = new Color32[vertexCount];\n                uniformColourScratch[vertexCount] = colours;\n            }\n            else operationHealthUniformColourReuses++;\n            for (int i = 0; i < colours.Length; i++) colours[i] = colour;\n            return colours;\n        }\n\n'''
    if anchor not in s: raise SystemExit('anchor not found: helper insertion')
    s=s.replace(anchor,helper+anchor,1)

rep('''        static void ProjectMesh(Mesh mesh, GeographicUnitPoint[] points,\n            Vector3[] projectedVertices, AERISNdMapProjection context)''','''        void ProjectMesh(Mesh mesh, GeographicUnitPoint[] points,\n            Vector3[] projectedVertices, AERISNdMapProjection context)''','void ProjectMesh(Mesh mesh')
rep('''            mesh.vertices = projectedVertices;\n            mesh.RecalculateBounds();''','''            mesh.vertices = projectedVertices;\n            operationHealthBoundsSkips++;''','operationHealthBoundsSkips++;')

old='''            bool rendered = false;\n            if (entry.WaterMesh != null && terrainMaterial.SetPass(0))\n            {\n                Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);\n                rendered = true;\n            }\n            if (entry.LandMesh != null && terrainMaterial.SetPass(0))\n            {\n                Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);\n                rendered = true;\n            }\n            // Candidate8 painter-order correction: the sparse 129-derived coastal band\n            // overwrites only coarse shoreline parent cells. Water is applied first, then\n            // land, matching the normal surface ordering while avoiding a full-HD tile.\n            if (entry.CoastalWaterCorrectionMesh != null && terrainMaterial.SetPass(0))\n            {\n                Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);\n                rendered = true;\n            }\n            if (entry.CoastalLandCorrectionMesh != null && terrainMaterial.SetPass(0))\n            {\n                Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);\n                rendered = true;\n            }'''
new='''            bool rendered = false;\n            int terrainMeshCount = (entry.WaterMesh == null ? 0 : 1) +\n                (entry.LandMesh == null ? 0 : 1) +\n                (entry.CoastalWaterCorrectionMesh == null ? 0 : 1) +\n                (entry.CoastalLandCorrectionMesh == null ? 0 : 1);\n            if (terrainMeshCount > 0 && terrainMaterial.SetPass(0))\n            {\n                // Candidate8 painter order is unchanged: base water, base land, sparse\n                // coastal water, sparse coastal land. Pass 3 only removes redundant\n                // Material.SetPass calls between meshes using the identical material.\n                if (entry.WaterMesh != null) Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);\n                if (entry.LandMesh != null) Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);\n                if (entry.CoastalWaterCorrectionMesh != null)\n                    Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);\n                if (entry.CoastalLandCorrectionMesh != null)\n                    Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);\n                rendered = true;\n                operationHealthTerrainSetPassSaved += Math.Max(0, terrainMeshCount - 1);\n            }'''
rep(old,new,'operationHealthTerrainSetPassSaved +=')

rep('''        static void EnsureWaterColour(Entry entry,\n            AERISTerrainColourPreset preset)''','''        void EnsureWaterColour(Entry entry,\n            AERISTerrainColourPreset preset)''','void EnsureWaterColour(Entry entry')
rep('''        static void ApplyUniformMeshColour(Mesh mesh, Color32 colour)\n        {\n            if (mesh == null || mesh.vertexCount <= 0) return;\n            var colours = new Color32[mesh.vertexCount];\n            for (int i = 0; i < colours.Length; i++) colours[i] = colour;\n            mesh.colors32 = colours;\n        }''','''        void ApplyUniformMeshColour(Mesh mesh, Color32 colour)\n        {\n            if (mesh == null || mesh.vertexCount <= 0) return;\n            mesh.colors32 = GetUniformColourScratch(mesh.vertexCount, colour);\n        }''','GetUniformColourScratch(mesh.vertexCount, colour)')

old='''                \"; oh_surface_builder_reuse=\" + operationHealthSurfaceBuilderReuses +\n                \"; cpu_terrain_draw=0.\");'''
new='''                \"; oh_surface_builder_reuse=\" + operationHealthSurfaceBuilderReuses +\n                \"; oh_identity_index_hit=\" + operationHealthIdentityIndexHits +\n                \"; oh_identity_index_miss=\" + operationHealthIdentityIndexMisses +\n                \"; oh_uniform_colour_reuse=\" + operationHealthUniformColourReuses +\n                \"; oh_bounds_skip=\" + operationHealthBoundsSkips +\n                \"; oh_setpass_saved=\" + operationHealthTerrainSetPassSaved +\n                \"; cpu_terrain_draw=0.\");'''
rep(old,new,'oh_setpass_saved=')

# Explicit GPU release: the actual Pass 2 order clears entries before destroying the pool.
old='''            DestroyMeshPool();\n            completed.Clear();'''
new='''            DestroyMeshPool();\n            identityIndexCache.Clear();\n            uniformColourScratch.Clear();\n            completed.Clear();'''
rep(old,new,'identityIndexCache.Clear();')

p.write_text(s)
print('Operation Health Pass 3 patch applied')
