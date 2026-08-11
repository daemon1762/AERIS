#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text = renderer.read_text()

if 'oh_global_setpass_saved=' in text and 'bool DrawLayerBatches(' in text:
    print('[AERIS23 Global Layer Pass Batching] already applied')
    raise SystemExit(0)


def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old, new, 1)

# Persistent batch scratch + telemetry. No per-BACK managed allocation.
text = replace_once(text,
'''        long operationHealthUniformColourReuses;
        long operationHealthBoundsSkips;
        long operationHealthTerrainSetPassSaved;''',
'''        long operationHealthUniformColourReuses;
        long operationHealthBoundsSkips;
        long operationHealthTerrainSetPassSaved;
        // AERIS23 Global Layer Pass Batching: preserve the exact Entry meshes and
        // painter data, but submit one material layer across all visible Entries before
        // switching material. Scratch arrays are persistent to keep the 10 Hz path GC-free.
        long operationHealthGlobalLayerBatches;
        long operationHealthGlobalSetPassSaved;
        long operationHealthDrawMeshSubmissions;
        long operationHealthLayerBatchScratchResizes;
        Entry[] layerBatchEntriesScratch = new Entry[0];
        Matrix4x4[] layerBatchMatricesScratch = new Matrix4x4[0];
        AERISTerrainTileLod[] layerBatchLodsScratch = new AERISTerrainTileLod[0];''',
'global layer batch fields')

old_render = '''                bool entryCullingEnabled = ResolveViewportCullCap(vessel.mainBody,
                    rangeMeters, anchorV, out viewportCullSin, out viewportCullCos);
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?
                        drawEntries[i] : null;
                    if (drawEntry == null) continue;
                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos)) continue;
                    operationHealthPreparedEntryUses++;
                    Matrix4x4 projectionBridge = EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg, forceCenterProjectionRefresh);
                    // Cached geometry is N-UP. Apply the tiny center-motion bridge first,
                    // then the existing exact scale-corrected TRACK-UP rotation.
                    Matrix4x4 entryMapMatrix = mapRotation * projectionBridge;
                    bool entryRendered = DrawEntry(drawEntry, entryMapMatrix, true, effectiveMode,
                        settings == null ? AERISTerrainColourPreset.Standard :
                        settings.TerrainColourPreset, (float)vessel.altitude);
                    rendered = entryRendered || rendered;
                    if (entryRendered && tile.Key.Lod >= AERISTerrainTileLod.Route)
                        exactDetailOverlayDraws++;
                }'''
new_render = '''                bool entryCullingEnabled = ResolveViewportCullCap(vessel.mainBody,
                    rangeMeters, anchorV, out viewportCullSin, out viewportCullCos);
                EnsureLayerBatchScratch(tiles.Length);
                int layerBatchCount = 0;
                AERISTerrainColourPreset currentPreset = settings == null ?
                    AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?
                        drawEntries[i] : null;
                    if (drawEntry == null) continue;
                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos)) continue;
                    operationHealthPreparedEntryUses++;
                    Matrix4x4 projectionBridge = EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg, forceCenterProjectionRefresh);
                    // Cached geometry is N-UP. Apply the tiny center-motion bridge first,
                    // then the existing exact scale-corrected TRACK-UP rotation.
                    Matrix4x4 entryMapMatrix = mapRotation * projectionBridge;
                    // Colour uploads remain per Entry and retain their existing dirty guards.
                    // Only the subsequent material/draw submission order is batched.
                    EnsureLandColours(drawEntry, effectiveMode, currentPreset,
                        (float)vessel.altitude);
                    EnsureWaterColour(drawEntry, currentPreset);
                    layerBatchEntriesScratch[layerBatchCount] = drawEntry;
                    layerBatchMatricesScratch[layerBatchCount] = entryMapMatrix;
                    layerBatchLodsScratch[layerBatchCount] = tile.Key.Lod;
                    layerBatchCount++;
                }
                rendered = DrawLayerBatches(layerBatchCount, true);'''
text = replace_once(text, old_render, new_render, 'RenderBackBuffer layer preparation')

start = text.index('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
end = text.index('        void EnsureWaterColour(Entry entry,', start)
replacement = '''        void EnsureLayerBatchScratch(int minimumCount)
        {
            if (minimumCount <= layerBatchEntriesScratch.Length) return;
            int next = Math.Max(minimumCount,
                Math.Max(32, layerBatchEntriesScratch.Length * 2));
            Array.Resize(ref layerBatchEntriesScratch, next);
            Array.Resize(ref layerBatchMatricesScratch, next);
            Array.Resize(ref layerBatchLodsScratch, next);
            operationHealthLayerBatchScratchResizes++;
        }

        bool DrawLayerBatches(int count, bool drawContours)
        {
            if (count <= 0) return false;
            int terrainEntryCount = 0;
            int terrainMeshCount = 0;
            int contourEntryCount = 0;
            int coastlineEntryCount = 0;
            for (int i = 0; i < count; i++)
            {
                Entry entry = layerBatchEntriesScratch[i];
                if (entry == null) continue;
                int entryTerrainMeshes = (entry.WaterMesh == null ? 0 : 1) +
                    (entry.LandMesh == null ? 0 : 1) +
                    (entry.CoastalWaterCorrectionMesh == null ? 0 : 1) +
                    (entry.CoastalLandCorrectionMesh == null ? 0 : 1);
                if (entryTerrainMeshes > 0)
                {
                    terrainEntryCount++;
                    terrainMeshCount += entryTerrainMeshes;
                }
                if (drawContours && entry.ContourMesh != null) contourEntryCount++;
                if (entry.CoastlineMesh != null) coastlineEntryCount++;
            }

            bool rendered = false;
            long globalSaved = 0L;
            // Global painter order is intentional: every terrain surface first, then every
            // contour, then every coastline. Within each terrain Entry the accepted
            // Candidate8 order remains water, land, sparse coastal water, sparse coastal land.
            if (terrainEntryCount > 0 && terrainMaterial.SetPass(0))
            {
                for (int i = 0; i < count; i++)
                {
                    Entry entry = layerBatchEntriesScratch[i];
                    if (entry == null) continue;
                    Matrix4x4 mapMatrix = layerBatchMatricesScratch[i];
                    bool entryRendered = false;
                    if (entry.WaterMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                        entryRendered = true;
                    }
                    if (entry.LandMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                        entryRendered = true;
                    }
                    if (entry.CoastalWaterCorrectionMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                        entryRendered = true;
                    }
                    if (entry.CoastalLandCorrectionMesh != null)
                    {
                        Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);
                        operationHealthDrawMeshSubmissions++;
                        entryRendered = true;
                    }
                    if (entryRendered && layerBatchLodsScratch[i] >=
                        AERISTerrainTileLod.Route) exactDetailOverlayDraws++;
                    rendered = entryRendered || rendered;
                }
                // Preserve the old metric: savings between terrain meshes within Entries.
                operationHealthTerrainSetPassSaved +=
                    Math.Max(0, terrainMeshCount - terrainEntryCount);
                globalSaved += Math.Max(0, terrainEntryCount - 1);
            }

            if (drawContours && contourEntryCount > 0 && contourMaterial.SetPass(0))
            {
                for (int i = 0; i < count; i++)
                {
                    Entry entry = layerBatchEntriesScratch[i];
                    if (entry == null || entry.ContourMesh == null) continue;
                    Graphics.DrawMeshNow(entry.ContourMesh, layerBatchMatricesScratch[i]);
                    operationHealthDrawMeshSubmissions++;
                }
                globalSaved += Math.Max(0, contourEntryCount - 1);
            }

            if (coastlineEntryCount > 0 && coastlineMaterial.SetPass(0))
            {
                for (int i = 0; i < count; i++)
                {
                    Entry entry = layerBatchEntriesScratch[i];
                    if (entry == null || entry.CoastlineMesh == null) continue;
                    Graphics.DrawMeshNow(entry.CoastlineMesh, layerBatchMatricesScratch[i]);
                    operationHealthDrawMeshSubmissions++;
                }
                globalSaved += Math.Max(0, coastlineEntryCount - 1);
            }

            operationHealthGlobalLayerBatches++;
            operationHealthGlobalSetPassSaved += globalSaved;
            for (int i = 0; i < count; i++) layerBatchEntriesScratch[i] = null;
            return rendered;
        }

'''
text = text[:start] + replacement + text[end:]

text = replace_once(text,
'''                "; oh_bounds_skip=" + operationHealthBoundsSkips +
                "; oh_setpass_saved=" + operationHealthTerrainSetPassSaved +
                "; oh_cadence_defer=" + operationHealthCadenceDeferrals +''',
'''                "; oh_bounds_skip=" + operationHealthBoundsSkips +
                "; oh_setpass_saved=" + operationHealthTerrainSetPassSaved +
                "; oh_global_layer_batch=" + operationHealthGlobalLayerBatches +
                "; oh_global_setpass_saved=" + operationHealthGlobalSetPassSaved +
                "; oh_draw_mesh=" + operationHealthDrawMeshSubmissions +
                "; oh_layer_batch_resize=" + operationHealthLayerBatchScratchResizes +
                "; oh_cadence_defer=" + operationHealthCadenceDeferrals +''',
'layer batching telemetry')

renderer.write_text(text)

# Adapt the existing Pass 3 regression to the new global-layer contract.
pass3 = ROOT / 'Tools/selftest_v01800_operation_health_pass3_projection_draw_reduction.py'
p = pass3.read_text()
old = '''draw=R[R.index('bool DrawEntry('):R.index('void EnsureWaterColour',R.index('bool DrawEntry('))]
ck(draw.count('terrainMaterial.SetPass(0)') == 1,
   'terrain meshes share one material SetPass per entry')'''
new = '''draw=R[R.index('bool DrawLayerBatches('):R.index('void EnsureWaterColour',R.index('bool DrawLayerBatches('))]
ck(draw.count('terrainMaterial.SetPass(0)') == 1 and
   draw.count('contourMaterial.SetPass(0)') == 1 and
   draw.count('coastlineMaterial.SetPass(0)') == 1,
   'terrain/contour/coast materials each SetPass once per BACK layer batch')'''
if p.count(old) != 1:
    raise SystemExit('Pass3 SetPass contract anchor mismatch')
p = p.replace(old, new, 1)
p = p.replace("ck('oh_bounds_skip=' in R and 'oh_setpass_saved=' in R and\n   'oh_identity_index_hit=' in R and 'oh_uniform_colour_reuse=' in R,\n   'Pass 3 runtime telemetry is published')",
"ck('oh_bounds_skip=' in R and 'oh_setpass_saved=' in R and\n   'oh_global_setpass_saved=' in R and 'oh_draw_mesh=' in R and\n   'oh_identity_index_hit=' in R and 'oh_uniform_colour_reuse=' in R,\n   'Pass 3 runtime telemetry is published')", 1)
pass3.write_text(p)

batch_test = ROOT / 'Tools/selftest_v01800_operation_health_global_layer_pass_batching.py'
batch_test.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
render=R[R.index('bool RenderBackBuffer('):R.index('float MeasureFoundationGpuReadiness')]
batch=R[R.index('bool DrawLayerBatches('):R.index('void EnsureWaterColour',R.index('bool DrawLayerBatches('))]
ck('Entry[] layerBatchEntriesScratch = new Entry[0]' in R and
   'Matrix4x4[] layerBatchMatricesScratch = new Matrix4x4[0]' in R,
   'persistent Entry/matrix batch scratch exists')
ck('EnsureLayerBatchScratch(tiles.Length)' in render and 'new Entry[' not in render and
   'new Matrix4x4[' not in render,'BACK batching adds no per-frame Entry/matrix allocation')
ck('EnsureProjectedGeometry(drawEntry' in render and
   'layerBatchEntriesScratch[layerBatchCount] = drawEntry' in render,
   'cull/projection authority completes before layer submission')
ck('EnsureLandColours(drawEntry' in render and 'EnsureWaterColour(drawEntry' in render,
   'existing dirty-guarded colour authority remains per Entry')
ck(batch.count('terrainMaterial.SetPass(0)') == 1,
   'terrain material SetPass occurs once per BACK batch')
ck(batch.count('contourMaterial.SetPass(0)') == 1,
   'contour material SetPass occurs once per BACK batch')
ck(batch.count('coastlineMaterial.SetPass(0)') == 1,
   'coastline material SetPass occurs once per BACK batch')
terrain=batch.index('terrainMaterial.SetPass(0)')
contour=batch.index('contourMaterial.SetPass(0)')
coast=batch.index('coastlineMaterial.SetPass(0)')
ck(terrain < contour < coast,'global layer order is terrain then contour then coastline')
order=[
 'Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)',
 'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)'
]
pos=[batch.find(x) for x in order]
ck(all(x>=0 for x in pos) and pos==sorted(pos),
   'Candidate8 terrain painter order remains unchanged inside each Entry')
ck('operationHealthTerrainSetPassSaved +=' in batch and
   'terrainMeshCount - terrainEntryCount' in batch,
   'legacy within-Entry terrain SetPass-saved metric keeps its meaning')
ck('operationHealthGlobalSetPassSaved += globalSaved' in batch and
   'terrainEntryCount - 1' in batch and 'contourEntryCount - 1' in batch and
   'coastlineEntryCount - 1' in batch,'inter-Entry SetPass savings are separately observable')
ck('operationHealthDrawMeshSubmissions++' in batch and 'oh_draw_mesh=' in R,
   'remaining DrawMeshNow submission count is runtime observable')
ck('oh_global_layer_batch=' in R and 'oh_global_setpass_saved=' in R and
   'oh_layer_batch_resize=' in R,'global batching telemetry is published')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'render-target quality remains ARGB32 Bilinear')
ck('MaximumContourLevelsPerTile = 96' in
   (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(),
   'Candidate11 contour authority remains 96')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   '10 Hz authoritative presentation remains unchanged')
ck('Matrix4x4 EnsureProjectedGeometry(' in R and 'oh_project_bridge=' in R,
   'Projection Motion Bridge remains intact')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Global Layer Pass Batching] %d/%d PASS' %
      (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

prebuild = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
pb = prebuild.read_text()
marker = " ('Operation Health Pass 3 projection/draw reduction','selftest_v01800_operation_health_pass3_projection_draw_reduction.py'),"
if 'selftest_v01800_operation_health_global_layer_pass_batching.py' not in pb:
    if marker not in pb:
        raise SystemExit('prebuild Pass3 marker absent')
    pb = pb.replace(marker,
        " ('Operation Health Global Layer Pass Batching','selftest_v01800_operation_health_global_layer_pass_batching.py'),\n" + marker, 1)
    prebuild.write_text(pb)

print('[AERIS23 Global Layer Pass Batching] patch applied')
print('Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py')
print('Then: git diff --check')
