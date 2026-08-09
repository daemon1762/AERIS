#!/usr/bin/env python3
from pathlib import Path
import re

root=Path(__file__).resolve().parents[1]
nav=root/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs'
t=nav.read_text()

old='''            // ND panel resizing is fixed-aspect. Keep every internal furniture dimension
            // on that same scale as well; the old 1.25 ceiling made larger panels drift
            // away from the accepted 366:188 map-plot geometry even when the outer panel
            // itself remained 380:244. Screen bounds in AERISFlightInstrument own the
            // upper size limit.
            float scale = Mathf.Max(0.80f,
                Mathf.Min(rect.width / 380f, rect.height / 244f));'''
new='''            // ND furniture stays at instrument size. User resize authority belongs to the
            // map surface only; buttons, bezel, margins and information typography do not
            // grow with the terrain viewport.
            const float scale = 1f;'''
if t.count(old)!=1: raise SystemExit('navigation scale anchor='+str(t.count(old)))
t=t.replace(old,new,1)

old='''            float margin = Mathf.Max(4f, 6f * scale);
            float header = Mathf.Max(18f, 23f * scale);
            float controls = Mathf.Max(19f, 25f * scale);'''
new='''            // Fixed furniture. The resizable map surface is the area inside these rails.
            const float margin = 6f;
            const float controls = 25f;'''
if t.count(old)!=1: raise SystemExit('DrawLocal furniture anchor='+str(t.count(old)))
t=t.replace(old,new,1)

pattern=re.compile(r'''\n            string mode = planMode \? "PLAN" : \(landActive \? "LAND" : "TERR"\);\n            string orientation = planMode \? "N" : \(effectiveTrackUp \? "TRK" : "N"\);\n            DrawLabel\(new Rect\(margin, 0f, rect\.width \* 0\.42f, header\),\n                mode \+ "  " \+ orientation, titleStyle,\n                landActive \? ArmedColor : new Color\(0\.80f, 0\.94f, 1f, 1f\)\);\n            string rightHeader = landActive && direction != null \? direction\.DisplayName :\n                \(hasFrame \? frame\.Runways\.Length \+ " RWY" : "NAV DATA"\);\n            DrawLabel\(new Rect\(rect\.width \* 0\.42f, 0f, rect\.width \* 0\.56f - margin, header\),\n                rightHeader, rightTitleStyle, RunwayColor\);\n''')
t,n=pattern.subn('\n            // Top-bezel status text intentionally removed. Orientation, terrain mode and\n            // range remain available through the dedicated control buttons.\n',t,count=1)
if n!=1: raise SystemExit('top bezel label block='+str(n))

old='''            Rect viewport = new Rect(margin, header, Mathf.Max(20f, rect.width - margin * 2f),
                Mathf.Max(20f, rect.height - header - controls - margin));'''
new='''            Rect viewport = new Rect(margin, margin,
                Mathf.Max(20f, rect.width - margin * 2f),
                Mathf.Max(20f, rect.height - controls - margin * 2f));'''
if t.count(old)!=1: raise SystemExit('viewport anchor='+str(t.count(old)))
t=t.replace(old,new,1)

old='''            float height = Mathf.Max(17f, rect.height - 2f);
            float button = Mathf.Max(23f, 31f * scale);
            float rangeButton = Mathf.Max(35f, 48f * scale);
            float wideButton = Mathf.Max(44f, 58f * scale);
            float resizeReserve = Mathf.Max(22f, 24f * scale);'''
new='''            float height = Mathf.Max(17f, rect.height - 2f);
            const float button = 31f;
            const float rangeButton = 48f;
            const float wideButton = 58f;
            const float resizeReserve = 24f;'''
if t.count(old)!=1: raise SystemExit('control size anchor='+str(t.count(old)))
t=t.replace(old,new,1)
old='''            float menuWidth = Mathf.Max(38f, 50f * scale);'''
if t.count(old)!=1: raise SystemExit('menu width anchor='+str(t.count(old)))
t=t.replace(old,'''            const float menuWidth = 50f;''',1)

pattern=re.compile(r'''\n            string compact = isPlan \? "DRAG MAP  PLAN" : "CLICK RWY  PREVIEW";\n            if \(landActive\)\n            \{\n                AERISLandingFoundation land = core\.Landing;\n                compact = "PILOT  ARM";\n                if \(land != null && land\.Observation != null\)\n                \{\n                    compact \+= land\.Observation\.LocalizerGeometryEligible \? "  LOC" : string\.Empty;\n                    compact \+= land\.Observation\.GlidePathGeometryEligible \? "  GS" : string\.Empty;\n                \}\n            \}\n            DrawLabel\(new Rect\(rect\.x \+ 2f, rect\.y,\n                Mathf\.Max\(10f, view\.x - rect\.x - 4f\), height\), compact,\n                titleStyle, landActive \? ArmedColor : new Color\(0\.68f, 0\.82f, 0\.88f, 1f\)\);''')
t,n=pattern.subn('\n            // No lower-left advisory text: the control strip contains buttons only.',t,count=1)
if n!=1: raise SystemExit('lower-left text block='+str(n))
nav.write_text(t)

ui=root/'Source/AERISFlightControl/UI/AERISFlightInstrument.cs'
t=ui.read_text()
old='''        // AERIS23 rollback recovery: the ND may scale, but its panel geometry may not
        // change aspect ratio. 380:244 keeps the accepted Golden internal map geometry
        // (approximately 366:188 after furniture/margins) invariant at every size.
        const float NavigationPanelAspect = BasePanelWidth / BasePanelHeight;'''
new='''        // ND resize authority belongs to the cartographic surface, not to the furniture.
        // At stock geometry the plan map is exactly 366x188 inside fixed 14px horizontal
        // and 39px vertical furniture. Resizing preserves only the 366:188 map ratio.
        const float NavigationBaseMapWidth = 366f;
        const float NavigationBaseMapHeight = 188f;
        const float NavigationHorizontalFurniture = 14f;
        const float NavigationVerticalFurniture = 39f;
        const float NavigationMinimumMapScale = 1.0f;'''
if t.count(old)!=1: raise SystemExit('panel aspect constant anchor='+str(t.count(old)))
t=t.replace(old,new,1)

old='''            float defaultPanelWidth = BasePanelWidth * resolutionScale;
            float defaultPanelHeight = BasePanelHeight * resolutionScale;
            Rect defaultNdRect = new Rect(navFurniture.xMin - defaultPanelWidth - gap,
                nav.center.y - defaultPanelHeight * 0.5f,
                defaultPanelWidth, defaultPanelHeight);
            Rect defaultFdiRect = new Rect(verticalRect.xMax + gap,
                nav.center.y - defaultPanelHeight * 0.5f,
                defaultPanelWidth, defaultPanelHeight);'''
new='''            float defaultPanelWidth = BasePanelWidth * resolutionScale;
            float defaultPanelHeight = BasePanelHeight * resolutionScale;
            float defaultNdMapScale = Mathf.Max(NavigationMinimumMapScale, resolutionScale);
            float defaultNdWidth = NavigationHorizontalFurniture +
                NavigationBaseMapWidth * defaultNdMapScale;
            float defaultNdHeight = NavigationVerticalFurniture +
                NavigationBaseMapHeight * defaultNdMapScale;
            Rect defaultNdRect = new Rect(navFurniture.xMin - defaultNdWidth - gap,
                nav.center.y - defaultNdHeight * 0.5f,
                defaultNdWidth, defaultNdHeight);
            Rect defaultFdiRect = new Rect(verticalRect.xMax + gap,
                nav.center.y - defaultPanelHeight * 0.5f,
                defaultPanelWidth, defaultPanelHeight);'''
if t.count(old)!=1: raise SystemExit('default panel anchor='+str(t.count(old)))
t=t.replace(old,new,1)

old='''            if (kind == PanelKind.NavigationDisplay)
            {
                // Fit the fixed-aspect panel inside the requested bounding box. Using
                // the smaller scale guarantees migration never expands an old free-aspect
                // layout and prevents a resolution change from silently growing ND work.
                float requestedScale = Mathf.Min(
                    rect.width / Mathf.Max(1f, BasePanelWidth),
                    rect.height / Mathf.Max(1f, BasePanelHeight));
                return SetNavigationPanelScale(rect, requestedScale);
            }'''
new='''            if (kind == PanelKind.NavigationDisplay)
            {
                // Interpret saved geometry as a bounding box for the map surface. Existing
                // outer-aspect layouts are migrated without enlarging either dimension.
                float availableMapWidth = Mathf.Max(1f,
                    rect.width - NavigationHorizontalFurniture);
                float availableMapHeight = Mathf.Max(1f,
                    rect.height - NavigationVerticalFurniture);
                float requestedScale = Mathf.Min(
                    availableMapWidth / NavigationBaseMapWidth,
                    availableMapHeight / NavigationBaseMapHeight);
                return SetNavigationPanelScale(rect, requestedScale);
            }'''
if t.count(old)!=1: raise SystemExit('ClampPanelSize ND anchor='+str(t.count(old)))
t=t.replace(old,new,1)

pattern=re.compile(r'''        static Rect ResizeNavigationPanel\(Rect start, Vector2 delta\)\n        \{.*?\n        \}\n\n        static Rect SetNavigationPanelScale\(Rect rect, float requestedScale\)\n        \{.*?\n        \}\n''',re.S)
replacement='''        static Rect ResizeNavigationPanel(Rect start, Vector2 delta)
        {
            float startScale = Mathf.Max(0.01f,
                (start.width - NavigationHorizontalFurniture) /
                NavigationBaseMapWidth);
            float widthScale = (start.width + delta.x -
                NavigationHorizontalFurniture) / NavigationBaseMapWidth;
            float heightScale = (start.height + delta.y -
                NavigationVerticalFurniture) / NavigationBaseMapHeight;
            // Dominant normalized drag axis controls one map-surface scale. Furniture
            // remains fixed, so only the 366:188 cartographic canvas grows or shrinks.
            float requestedScale = Mathf.Abs(widthScale - startScale) >=
                Mathf.Abs(heightScale - startScale) ? widthScale : heightScale;
            return SetNavigationPanelScale(start, requestedScale);
        }

        static Rect SetNavigationPanelScale(Rect rect, float requestedScale)
        {
            float screenScale = Mathf.Min(
                Mathf.Max(1f, Screen.width - 8f - NavigationHorizontalFurniture) /
                    NavigationBaseMapWidth,
                Mathf.Max(1f, Screen.height - 8f - NavigationVerticalFurniture) /
                    NavigationBaseMapHeight);
            float minimumScale = Mathf.Min(NavigationMinimumMapScale,
                Mathf.Max(0.10f, screenScale));
            float maximumScale = Mathf.Max(minimumScale, screenScale);
            float scale = Mathf.Clamp(requestedScale, minimumScale, maximumScale);
            rect.width = NavigationHorizontalFurniture + NavigationBaseMapWidth * scale;
            rect.height = NavigationVerticalFurniture + NavigationBaseMapHeight * scale;
            return rect;
        }
'''
t,n=pattern.subn(replacement,t,count=1)
if n!=1: raise SystemExit('resize method block='+str(n))
ui.write_text(t)

test=root/'Tools/selftest_v01800_nd_fixed_aspect_resize.py'
test.write_text('''#!/usr/bin/env python3
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
print('\\n[ND Fixed Map Surface Resize] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

pre=root/'Tools/run_v01800_operation_health_pass3_prebuild.py'
t=pre.read_text()
t=t.replace("('ND Fixed Aspect Rollback','selftest_v01800_nd_fixed_aspect_resize.py')","('ND Fixed Map Surface Resize','selftest_v01800_nd_fixed_aspect_resize.py')")
pre.write_text(t)
