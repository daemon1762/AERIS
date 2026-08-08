#!/usr/bin/env python3
from pathlib import Path
import math,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
P=(ROOT/'Source/AERISFlightControl/Terrain/AERISNdMapProjection.cs').read_text()
F=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainViewportFoundationPlanner.cs').read_text()
S=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs').read_text()
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('ReferencePlotWidthPixels = 366f' in P and 'ReferencePlotHeightPixels = 188f' in P,'default ND plot is explicit scale reference')
ck('ResolveMetersPerPixel' in P and 'ResolveAspectCorrectExtents' in P,'aspect-correct scale resolver exists')
ck('horizontalMeters = Math.Max(1.0, Math.Max(1f, plotWidthPixels) *' in P and 'verticalMeters = Math.Max(1.0, Math.Max(1f, plotHeightPixels) *' in P,'horizontal and vertical extents share one metres-per-pixel authority')
mpp=80000.0/188.0; h=mpp*366.0; v=mpp*188.0
ck(abs(h/366.0-v/188.0)<1e-9 and abs(v-80000.0)<1e-6,'80 km nominal view has equal X/Y metres per pixel')
ck(abs((h*2)/732.0-mpp)<1e-9 and abs((v*2)/376.0-mpp)<1e-9,'doubling window adds geography without changing scale')
ck('CreateWithExtents' in F and 'horizontalMeters, verticalMeters' in F,'foundation planner consumes aspect extents')
ck('MaximumFarKeys = 1024' in F and '), 4, 96);' in F,'foundation capacity supports enlarged viewport')
ck('displayViewHorizontalMeters' in S and 'displayViewVerticalMeters' in S,'tile system stores geographic viewport extents')
ck('double horizontalMeters' in S and 'double verticalMeters' in S,'CaptureVisible receives exact aspect extents')
ck('requestedHorizontalMeters, requestedVerticalMeters' in R,'renderer passes exact aspect extents into content authority')
ck('ResolveAspectCorrectExtents(rangeMeters, plot.width' in R and 'CreateWithExtents(vessel.mainBody' in R,'terrain projection consumes actual plot geometry')
ck('frontPlotWidthPixels' in R and 'FrontPlotMatches(plot)' in R,'committed FRONT records exact plot geometry')
fast=R[R.index('if (!authoritativeTickDue)'):R.index('residentCache = system.CurrentBodyResidentCache;')]
ck('operationHealthAspectResizeDeferrals++' in fast and 'return lastDrawState;' in fast,'resize mismatch defers without stretching old FRONT')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'resize does not violate fixed 10 Hz authority')
ck('context.HorizontalMeters' in R and 'LastProjectionHorizontalMeters' in R,'width-only resize invalidates cached projected geometry')
cull=R[R.index('bool ShouldCullEntryOutsidePresentation('):R.index('static void ResolveConservativeEntryBounds(')]
ck('projection.HorizontalMeters * 0.5' in cull and 'projection.VerticalMeters * Math.Max' in cull,'entry culling uses actual aspect-correct viewport')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target visual quality authority unchanged')
ck('oh_aspect_resize_defer=' in R and 'aspect_hv=' in R and 'aspect_plot=' in R,'aspect runtime telemetry is published')
failed=[n for ok,n in checks if not ok]
print('\n[Aspect-Correct Map Core] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
