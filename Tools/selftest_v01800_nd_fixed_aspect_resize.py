#!/usr/bin/env python3
from pathlib import Path
import math,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
U=(ROOT/'Source/AERISFlightControl/UI/AERISFlightInstrument.cs').read_text()
P=(ROOT/'Source/AERISFlightControl/Terrain/AERISNdMapProjection.cs').read_text()
F=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainViewportFoundationPlanner.cs').read_text()
V=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs').read_text()
C=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs').read_text()
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('const float BasePanelWidth = 380f;' in U and 'const float BasePanelHeight = 244f;' in U,
   'ND fixed ratio uses accepted 380x244 panel baseline')
ck('NavigationPanelAspect = BasePanelWidth / BasePanelHeight' in U,
   'fixed aspect authority is explicit')
ck('ClampPanelSize(PanelKind kind, Rect rect)' in U and
   'kind == PanelKind.NavigationDisplay' in U,
   'ND size clamp is panel-kind aware')
ck('ResizeNavigationPanel(interactionStartRect, delta)' in U,
   'ND resize uses dedicated uniform scale path')
ck('widthScale' in U and 'heightScale' in U and
   'Mathf.Abs(widthScale - startScale)' in U,
   'dominant normalized drag axis controls uniform scaling')
ck('rect.width = BasePanelWidth * scale;' in U and
   'rect.height = BasePanelHeight * scale;' in U,
   'every ND resize writes both dimensions from one scale')
N=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text()
ck('Mathf.Max(0.80f,' in N and '1.25f' not in N[N.index('internal void Draw(Rect rect)'):N.index('void EnsureStyles')],
   'ND internal layout follows panel scale above 1.25 without geometry drift')
ck('float minimumScale = Mathf.Max(0.80f' in U,
   'ND minimum size stays above internal readability-floor distortion threshold')
ck('requestedScale = Mathf.Min(' in U and
   'PersistPanelRect(kind, rect, false);' in U,
   'legacy free-aspect ND layouts migrate without enlargement')
ck('NavigationPanelAspect' in U and abs(380.0/244.0-1.55737704918)<1e-9,
   'numerical panel aspect is 380:244')
# PR15 rollback markers: accepted pre-aspect projection/planner behavior.
ck('HorizontalMeters = Math.Max(1.0, rangeMeters * 1.30)' in P and
   'ResolveAspectCorrectExtents' not in P,
   'PR15 aspect-dependent geographic expansion is removed')
ck('MaximumFarKeys = 192' in F and 'MaximumFarKeys = 1024' not in F,
   'foundation capacity returns to pre-PR15 bounded model')
ck('GOLDEN LOW 65' not in V,
   'runtime-failed PR16 whole-tile 65 reconstruction is absent')
ck('BuildTopologyPreservingCoastalPresentationMask' not in C,
   'runtime-failed PR17 synthetic coastline fallback is absent')
ck('oh_aspect_resize_defer=' not in R and 'aspect_hv=' not in R,
   'PR15 resize-expansion telemetry/path is absent')
# A uniformly scaled panel preserves its exact aspect at representative sizes.
for s in (0.58,1.0,1.5,2.0):
    w,h=380.0*s,244.0*s
    ck(abs(w/h-380.0/244.0)<1e-12,'uniform scale %.2f preserves ND aspect' % s)
failed=[n for ok,n in checks if not ok]
print('\n[ND Fixed Aspect Rollback] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
