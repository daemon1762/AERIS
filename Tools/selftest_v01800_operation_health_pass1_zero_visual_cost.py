#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
renderer=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
raster=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
checks=[]
def ck(v,n):
    checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)

# Pass 1 management-cost reductions.
ck('Dictionary<AERISTerrainTileKey, List<Entry>> entriesByTile' in renderer,
   'GPU entries have TileKey-local lookup index')
resolve=renderer[renderer.index('void ResolveRenderableEntries'):renderer.index('void AddEntry')]
ck('entriesByTile.TryGetValue(tile.Key' in resolve,
   'entry resolution uses TileKey bucket')
ck('foreach (Entry candidate in entries.Values)' not in resolve,
   'entry resolution no longer scans all GPU entries')
ck('operationHealthResolveCandidates += bucket.Count' in resolve,
   'entry candidate work is runtime-observable')
ck('void AddEntry(Entry entry)' in renderer and 'entriesByTile[entry.TileKey] = bucket' in renderer,
   'entry insert maintains TileKey index')
remove=renderer[renderer.index('void Remove(Entry entry)'):renderer.index('void FailGpuTerrain')]
ck('entriesByTile.TryGetValue(entry.TileKey' in remove and 'bucket.Remove(entry)' in remove,
   'entry removal maintains TileKey index')
ck('visible.Tiles.Clone()' not in renderer,
   'per-repaint visible tile Clone allocation removed')
ck('PrepareSortedTileScratch(visible.Tiles)' in renderer,
   'reusable sorted tile scratch is used')
ck('Entry[] currentEntriesScratch' in renderer and 'Entry[] drawEntriesScratch' in renderer,
   'resolved entries are cached for the repaint')
ck('MeasureFoundationGpuReadiness(visible, tiles,\n                currentEntriesScratch' in renderer,
   'foundation readiness consumes prepared current entries')
ck('RenderBackBuffer(tiles, drawEntriesScratch, projection' in renderer,
   'back rendering consumes prepared draw entries')
ck('scheduledThisFrame' in renderer and 'cacheKey + "|PENDING"' not in renderer,
   'pending schedule marker string allocation removed')
ck('TryUploadRenderReadyField(tile, cacheKey, styleKey' in renderer and
   'Schedule(tile, cacheKey, styleKey' in renderer,
   'one per-tile cache key is shared with upload and schedule')
ck('double currentCenterLatitudeDeg, double currentCenterLongitudeDeg' in renderer,
   'projection center is passed into per-entry projection test')
ck('UnitLatitude(context.CenterX, context.CenterY, context.CenterZ)' not in
   renderer[renderer.index('static void EnsureProjectedGeometry'):renderer.index('static void ProjectMesh')],
   'per-entry projection center trig recomputation removed')
ck('oh_resolve_calls=' in renderer and 'oh_resolve_candidates=' in renderer and
   'oh_tile_scratch_resize=' in renderer,
   'Operation Health telemetry is published')

# Candidate11 visual authority must remain unchanged in Pass 1.
ck('MaximumContourLevelsPerTile = 96' in raster,
   'Candidate11 contour level budget remains 96')
ck('HighDensityPointIsLand' in raster and 'AppendContourSegment' in raster,
   'Candidate11 HD coastal contour clipping remains enabled')
ck('Math.Min(16, Math.Max(0, last - first + 1))' not in raster,
   'legacy per-triangle contour truncation remains removed')
ck('const float RelativeAltitudeBucketMeters = 5f' in renderer,
   'REL colour altitude quantization unchanged')
ck('new RenderTexture(width, height, 0,\n                RenderTextureFormat.ARGB32)' in renderer,
   'ARGB32 FRONT/BACK render-target format unchanged')
ck('backTarget.filterMode = FilterMode.Bilinear' in renderer and
   'frontTarget.filterMode = FilterMode.Bilinear' in renderer,
   'render-target filtering unchanged')
ck('nextBackRefreshRealtime = Time.realtimeSinceStartup + 0.10f' in renderer,
   'Candidate11 back refresh cadence unchanged')
ck('ProjectionRefreshAgeSeconds = 0.50f' in renderer and
   'ProjectionRefreshHeadingDeg = 8f' in renderer,
   'Candidate11 projection refresh thresholds unchanged')

failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 1] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
