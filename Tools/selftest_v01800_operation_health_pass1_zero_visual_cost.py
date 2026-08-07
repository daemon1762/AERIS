#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
renderer=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
F=''.join(renderer.split())
raster=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('Dictionary<AERISTerrainTileKey,List<Entry>>entriesByTile' in F,'GPU entries have TileKey-local lookup index')
resolve=renderer[renderer.index('void ResolveRenderableEntries'):renderer.index('void AddEntry')]
RF=''.join(resolve.split())
ck('entriesByTile.TryGetValue(tile.Key' in resolve,'entry resolution uses TileKey bucket')
ck('foreach(Entrycandidateinentries.Values)' not in RF,'entry resolution no longer scans all GPU entries')
ck('operationHealthResolveCandidates+=bucket.Count' in RF,'entry candidate work is runtime-observable')
ck('voidAddEntry(Entryentry)' in F and 'entriesByTile[entry.TileKey]=bucket' in F,'entry insert maintains TileKey index')
remove=renderer[renderer.index('void Remove(Entry entry)'):renderer.index('void FailGpuTerrain')]
RM=''.join(remove.split())
ck('entriesByTile.TryGetValue(entry.TileKey' in remove and 'bucket.Remove(entry)' in remove,'entry removal maintains TileKey index')
ck('visible.Tiles.Clone()' not in renderer,'per-repaint visible tile Clone allocation removed')
ck('PrepareSortedTileScratch(visible.Tiles)' in renderer,'reusable sorted tile scratch is used')
ck('Entry[]currentEntriesScratch' in F and 'Entry[]drawEntriesScratch' in F,'resolved entries are cached for repaint/content snapshot')
ck('MeasureFoundationGpuReadiness(visible,tiles,currentEntriesScratch,outreadyGlobal,outreadyFar)' in F,'foundation readiness consumes prepared current entries')
ck('RenderBackBuffer(tiles,drawEntriesScratch,projection' in F,'main exact back rendering consumes prepared draw entries')
schedule=renderer[renderer.index('void Schedule('):renderer.index('void DrainCompleted(')]
SF=''.join(schedule.split())
ck('scheduledThisFrame.Add(cacheKey)' in schedule and 'requested.Contains(cacheKey+"|PENDING")' not in SF and 'requested.Add(cacheKey+"|PENDING")' not in SF,'pending schedule marker string allocation removed')
ck('TryUploadRenderReadyField(tile,cacheKey,styleKey' in F and 'Schedule(tile,cacheKey,styleKey' in F,'one per-tile cache key is shared with upload and schedule')
ck('doublecurrentCenterLatitudeDeg,doublecurrentCenterLongitudeDeg' in F,'projection center is passed into per-entry projection test')
projection_block=renderer[renderer.index('void EnsureProjectedGeometry'):renderer.index('void ProjectMesh')]
ck('UnitLatitude(context.CenterX,context.CenterY,context.CenterZ)' not in ''.join(projection_block.split()),'per-entry projection center trig recomputation removed')
ck('oh_resolve_calls=' in renderer and 'oh_resolve_candidates=' in renderer and 'oh_tile_scratch_resize=' in renderer,'Operation Health telemetry is published')
ck('MaximumContourLevelsPerTile = 96' in raster,'Candidate11 contour level budget remains 96')
ck('HighDensityPointIsLand' in raster and 'AppendContourSegment' in raster,'Candidate11 HD coastal contour clipping remains enabled')
ck('Math.Min(16, Math.Max(0, last - first + 1))' not in raster,'legacy per-triangle contour truncation remains removed')
ck('constfloatRelativeAltitudeBucketMeters=5f' in F,'REL colour altitude quantization unchanged')
ck('newRenderTexture(width,height,0,RenderTextureFormat.ARGB32)' in F,'ARGB32 FRONT/BACK render-target format unchanged')
ck('backTarget.filterMode=FilterMode.Bilinear' in F and 'frontTarget.filterMode=FilterMode.Bilinear' in F,'render-target filtering unchanged')
ck(('nextBackRefreshRealtime=Time.realtimeSinceStartup+0.10f' in F) or ('nextBackRefreshRealtime=nextAuthoritativePresentationTickRealtime' in F and 'nextAuthoritativePresentationTickRealtime=presentationNow+0.10f' in F),'Candidate11 back refresh cadence remains 0.10 seconds under approved shared authority')
ck('ProjectionRefreshAgeSeconds=0.50f' in F and 'ProjectionRefreshHeadingDeg=8f' in F,'Candidate11 projection refresh thresholds unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 1] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
