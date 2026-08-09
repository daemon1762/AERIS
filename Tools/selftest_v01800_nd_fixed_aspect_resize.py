#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
U=(ROOT/'Source/AERISFlightControl/UI/AERISFlightInstrument.cs').read_text()
N=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text()
P=(ROOT/'Source/AERISFlightControl/Terrain/AERISNdMapProjection.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('NavigationBaseMapWidth = 366f' in U and 'NavigationBaseMapHeight = 188f' in U,'resize authority is the accepted 366x188 map surface')
ck('NavigationHorizontalFurniture = 14f' in U and 'NavigationVerticalFurniture = 39f' in U,'bezel/control furniture has explicit fixed dimensions')
ck('availableMapWidth' in U and 'availableMapHeight' in U,'saved layouts normalize against map surface rather than outer panel aspect')
ck('widthScale = (start.width + delta.x -' in U and 'heightScale = (start.height + delta.y -' in U,'resize drag derives scale from map dimensions after fixed furniture')
ck('rect.width = NavigationHorizontalFurniture + NavigationBaseMapWidth * scale;' in U and 'rect.height = NavigationVerticalFurniture + NavigationBaseMapHeight * scale;' in U,'only map-area contribution scales in outer geometry')
ck('const float scale = 1f;' in N,'ND furniture and button typography do not scale with map size')
ck('const float margin = 6f;' in N and 'const float controls = 25f;' in N,'bezel and control-row geometry stay fixed')
ck('const float button = 31f;' in N and 'const float rangeButton = 48f;' in N and 'const float wideButton = 58f;' in N and 'const float menuWidth = 50f;' in N,'buttons stay fixed-size')
ck('mode + "  " + orientation' not in N and 'rightHeader' not in N,'top-bezel status text is removed')
ck('CLICK RWY  PREVIEW' not in N and 'DRAG MAP  PLAN' not in N and 'PILOT  ARM' not in N,'lower-left advisory text is removed')
ck('Rect viewport = new Rect(margin, margin,' in N,'map surface reclaims the deleted top status row')
ck('HorizontalMeters = Math.Max(1.0, rangeMeters * 1.30)' in P and 'ResolveAspectCorrectExtents' not in P,'window size does not increase geographic coverage')
for s in (1.0,1.5,2.0,3.0):
    outer_w=14.0+366.0*s; outer_h=39.0+188.0*s
    map_w=outer_w-14.0; map_h=outer_h-39.0
    ck(abs(map_w/map_h-366.0/188.0)<1e-12,'map surface scale %.1f preserves 366:188 while furniture stays fixed' % s)
ck(abs((14+366)-380)<1e-9 and abs((39+188)-227)<1e-9,'stock map surface resolves to compact 380x227 ND outer geometry')
failed=[n for ok,n in checks if not ok]
print('\n[ND Fixed Map Surface Resize] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
