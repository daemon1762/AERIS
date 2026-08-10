#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
start=R.index('bool ShouldCullEntryOutsidePresentation')
end=R.index('static bool ResolveViewportCullCap',start)
hot=R[start:end]
ck('GreatCircleDistanceMeters' not in hot and 'Math.Atan2' not in hot and 'Math.Sqrt' not in hot, 'per-entry cull hot path has no great-circle/trig/sqrt distance work')
ck('BoundCenterX' in R and 'BoundCenterY' in R and 'BoundCenterZ' in R, 'entry cap center unit vector is precomputed')
ck('BoundRadiusSin' in R and 'BoundRadiusCos' in R and 'ResolveSphericalCapFastData' in R, 'entry radius trig is precomputed once')
ck('projection.CenterX' in R and 'projection.CenterY' in R and 'projection.CenterZ' in R, 'BACK path reuses projection center unit vector')
ck('viewportRadius * 1.08' in R and 'Math.Max(2500.0' in R, 'accepted viewport safety margins remain')
ck('radius * 1.10 + 0.0005' in R, 'accepted entry-bound inflation remains')
ck('viewportSafetyRadius / body.Radius + 0.000001' in R, 'dot-cap adds only safe-direction angular pad')
ck('dot < thresholdCos' in hot, 'only mathematically disjoint conservative caps are rejected')
ck('rangeMeters < 120000f' not in R, '160 km no longer bypasses cheap culling')
render=R[R.index('bool RenderBackBuffer'):R.index('float MeasureFoundationGpuReadiness') ]
ck(render.index('ShouldCullEntryOutsidePresentation') < render.index('EnsureProjectedGeometry'), 'cull remains before projection/upload')
ck(render.index('ShouldCullEntryOutsidePresentation') < render.index('DrawEntry'), 'cull remains before draw submission')
ck('oh_dot_cap_test=' in R, 'runtime can prove fast path activity')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Dot-Cap Culling] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
