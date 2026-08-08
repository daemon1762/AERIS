#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('BoundAngularRadiusRad = Math.PI' in R,'invalid/large entry bounds default to never-cull radius')
ck('ResolveConservativeEntryBounds(' in R and 'AngularDistanceRadians(' in R,'spherical entry bounds are precomputed at upload time')
helper=R[R.index('bool ShouldCullEntryOutsidePresentation('):R.index('static void ResolveConservativeEntryBounds(')]
ck('viewportRadius * 1.08' in helper and 'Math.Max(2500.0' in helper,'viewport culling has multiplicative and absolute safety margins')
ck('entry.BoundAngularRadiusRad >= Math.PI * 0.50' in helper,'hemispheric/uncertain entries are never culled')
ck('centerDistance - entryRadiusMeters > viewportSafetyRadius' in helper,'whole entry is rejected only when its full spherical bound is outside safety radius')
render=R[R.index('bool RenderBackBuffer('):R.index('float MeasureFoundationGpuReadiness(')]
pos_cull=render.index('ShouldCullEntryOutsidePresentation(')
pos_project=render.index('EnsureProjectedGeometry(')
pos_draw=render.index('DrawEntry(')
ck(pos_cull < pos_project < pos_draw,'culling occurs before projection, mesh upload and draw')
ck('bool entryCullingEnabled = rangeMeters < 120000f;' in render,'160 km preset bypasses spherical entry culling')
ck('if (entryCullingEnabled &&' in render and 'ShouldCullEntryOutsidePresentation' in render,'narrow views retain conservative entry culling')
ck('operationHealthWideRangeCullBypassFrames++' in render,'wide-range bypass is runtime observable')
ck('float anchorV, bool forceCenterProjectionRefresh' in render,'exact ND anchor participates in conservative viewport radius')
ck('operationHealthCullTests++' in R and 'operationHealthCulledEntries++' in R and 'operationHealthVisibleEntries++' in R,'culling is runtime observable')
ck('oh_cull_test=' in R and 'oh_culled_entry=' in R and 'oh_visible_entry=' in R,'culling telemetry is published')
ck('oh_cull_wide_bypass=' in R,'wide-range culling bypass telemetry is published')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'visual RenderTexture quality authority unchanged')
ck('MaximumContourLevelsPerTile = 96' in (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(),'Candidate11 contour authority remains 96')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'10 Hz authoritative cadence unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Conservative Entry Culling] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
