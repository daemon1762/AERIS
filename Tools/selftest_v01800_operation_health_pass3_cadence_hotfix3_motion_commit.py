#!/usr/bin/env python3
from pathlib import Path
import json,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
B=(ROOT/'build_ubuntu.sh').read_text()
V=json.loads((ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text())
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,'ND authority remains fixed 10 Hz')
ck('const float ProjectionRefreshAgeSeconds = 0.50f;' in R,'0.50s projection fallback remains intact')
ck('const float ProjectionRefreshHeadingDeg = 8f;' in R,'legacy 8 degree safety threshold remains intact')
ck('AuthoritativeMotionSpeedMetersPerSecond = 0.5f' in R and 'AuthoritativeMotionDistanceMeters = 0.01' in R,'moving-map 10 Hz motion guard exists')
ck('bool NeedsAuthoritativeMotionRefresh(' in R,'dedicated moving-map refresh path exists')
helper=R[R.index('bool NeedsAuthoritativeMotionRefresh('):R.index('bool NeedsProjectionRefresh(')]
ck('vessel.srfSpeed >=' in helper and 'GreatCircleDistanceMeters' in helper,'motion refresh is grounded in actual speed and displacement')
ck('AuthoritativeMotionHeadingDeg' in helper and 'trackUp' in helper,'TRK UP small heading motion participates in 10 Hz commits')
draw=R[R.index('internal AERISTerrainGpuDrawState Draw('):R.index('bool NeedsContentRefresh(')]
ck('authoritativeMotionRefreshRequired ||' in draw and 'NeedsProjectionRefresh' in draw,'motion refresh augments rather than replaces fallback refresh authority')
ck('forceCenterProjectionRefresh' in draw,'moving-center force flag reaches renderer')
render=R[R.index('bool RenderBackBuffer('):R.index('bool DrawEntry(')]
ck('bool forceCenterProjectionRefresh' in render and 'operationHealthForcedProjectionRefreshes++' in render,'BACK render observes forced moving-center projection')
project=R[R.index('void EnsureProjectedGeometry('):R.index('void ProjectMesh(')]
ck('bool forceCenterProjectionRefresh' in project and 'bool projectionChanged = forceCenterProjectionRefresh ||' in project,'subpixel movement bypasses old 0.25px projection hold')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'presentation remains bounded to authoritative 10 Hz')
ck('TryPresentCoalescedFront(plot, vessel)' in R,'non-tick Repaints still use cheap FRONT reuse')
ck('oh_motion_refresh=' in R and 'oh_forced_project=' in R,'motion-commit telemetry is published')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target quality authority unchanged')
RA=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
ck('MaximumContourLevelsPerTile = 96' in RA and 'MaximumSparseCorrectionParentCells = 256' in RA,'Candidate11 contour/coastal safety authorities unchanged')
ck(V.get('NAME') in ('AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 3 Motion Commit','AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State','AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement'),'runtime identity is Hotfix 3 or approved successor')
ck('OPERATION HEALTH' in B,'Ubuntu build identifies Operation Health lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 3 Motion Commit] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
