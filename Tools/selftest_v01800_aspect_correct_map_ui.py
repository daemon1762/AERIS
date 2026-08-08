#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
U=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
map_block=U[U.index('static bool TryMapPoint(double eastMeters, double northMeters, double rangeMeters,\n            double headingDeg, bool trackUp, Rect plot, float anchorV,'):U.index('static void ToLocalMeters(')]
ck('ResolveAspectCorrectExtents' in map_block,'GUI symbology uses shared aspect-correct scale authority')
ck('rangeMeters * 1.30' not in map_block,'legacy anisotropic 1.30 map scale removed from TryMapPoint')
drag=U[U.index('if (mapDragging)'):U.index('e.Use();',U.index('if (mapDragging)'))]
ck('ResolveAspectCorrectExtents' in drag and 'horizontalMeters' in drag and 'verticalMeters' in drag,'PLAN drag uses exact aspect-correct metres per pixel')
ck('range * 1.30' not in drag,'PLAN drag no longer assumes legacy horizontal scale')
helper=U[U.index('static bool RunwayMayIntersectVisibleMap'):U.index('static double CurrentRunwayDistanceMeters')]
ck('ResolveAspectCorrectExtents' in helper and 'plot.width' in helper and 'plot.height' in helper,'runway cheap rejection uses actual viewport geometry')
ck('rangeMeters * 0.65' not in helper,'runway rejection no longer assumes legacy half-width')
ck(U.count('centerEast, centerNorth, range, anchorV, plot)') >= 2,'runway draw and hit-test pass exact plot geometry')
ck(U.count('plot.width, plot.height, heading, trackUp, anchorV, orientation') >= 2,'exact runway projections use actual plot dimensions')
ck('AERISNdMapProjection.ResolveAspectCorrectExtents' in U,'UI shares terrain cartographic scale resolver')
failed=[n for ok,n in checks if not ok]
print('\n[Aspect-Correct Map UI] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
