#!/usr/bin/env python3
from pathlib import Path
import json,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
N=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text()
B=(ROOT/'build_ubuntu.sh').read_text()
V=json.loads((ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text())
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('bool requestedViewReady;' in R and 'internal bool RequestedViewReady' in R,'requested-view READY is independent renderer state')
inv=R[R.index('internal void InvalidatePendingForViewChange'):R.index('internal void SuspendViewport')]
ck('requestedViewReady = false;' in inv and 'lastDrawState = AERISTerrainGpuDrawState.Partial;' in inv,'view invalidation immediately becomes BUILDING/Partial')
ck('lastBackFoundationCoverage = 0f;' in inv and 'lastCoverageFraction = 0f;' in inv,'new-view loading progress resets instead of inheriting stale 99 percent')
ck('ndReloadGeneration++;' in inv,'successor view invalidation starts a new black-reload generation')
ck('ReleaseGpuResources' not in inv and 'ResetFrontBufferState' not in inv,'old FRONT resources remain retained for non-reload continuity/recovery')
swap=R[R.index('void SwapFrontAndBack('):R.index('bool IsFrontBufferCompatible(')]
ck('frontReloadGeneration = ndReloadGeneration;' in swap and 'requestedViewReady = true;' in swap and swap.index('frontReloadGeneration = ndReloadGeneration;') < swap.index('requestedViewReady = true;'),'only exact FRONT swap closes black reload and transitions requested view to READY')
fast=R[R.index('bool TryPresentCoalescedFront('):R.index('void MarkGpuContentDirty(')]
ck('if (Reloading) return false;' in fast and 'requestedViewReady = true' not in fast,'cheap non-tick FRONT reuse is blocked during reload and cannot falsely declare READY')
end=R[R.index('UpdateReadyBuildingWatchdog(present'):R.index('bool NeedsContentRefresh(')]
ck('present && requestedViewReady' in end and 'AERISTerrainGpuDrawState.Partial' in end,'renderer Complete still requires an exact requested-view READY FRONT')
reset=R[R.index('void ResetFrontBufferState('):R.index('void Schedule(')]
ck('requestedViewReady = false;' in reset,'true FRONT lifecycle reset clears requested-view READY')
ck('internal bool Reloading' in R and 'ReloadProgressPercent' in R,'successor publishes explicit reload state/progress')
ck('oh_loading_backdrop=' in R and 'oh_ready_transition=' in R and 'requested_view_ready=' in R and 'oh_nd_reload=' in R,'runtime loading/readiness plus black-reload telemetry is published')
ck('if (!Reloading && directCompatible)' in R and 'if (!present && !Reloading && colourCompatible' in R,'stale direct/latched FRONT cannot be shown while explicit reload is active')
ck('if (gpuState == AERISTerrainGpuDrawState.Partial)' in N and 'RELOADING ND\\n' in N and 'TERRAIN GPU BUILDING ' in N,'ND UI distinguishes black explicit reload from ordinary Partial BUILDING')
ck('terrainTileRenderer.LastDrawState == AERISTerrainGpuDrawState.Partial' in N,'TERR/GPU OFF cannot become an infinite black reload screen')
ck('terrainTileRenderer.InvalidatePendingForViewChange();' in N and 'RangeChangeDebounceSeconds = 0.35f' in N,'coalesced range changes enter requested-view black reload path')
ck('return Time.realtimeSinceStartup - frontCommittedRealtime <= 8.0f;' in R,'existing stale-FRONT continuity safety window remains unchanged outside explicit reload')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'10 Hz authoritative cadence remains unchanged')
ck('AuthoritativeMotionSpeedMetersPerSecond = 0.5f' in R and 'forceCenterProjectionRefresh' in R,'Hotfix 3 exact moving-map 10 Hz path remains intact')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target visual quality authority unchanged')
RA=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
ck('MaximumContourLevelsPerTile = 96' in RA and 'MaximumSparseCorrectionParentCells = 256' in RA,'Candidate11 contour/coastal authorities unchanged')
ck(V.get('NAME') in ('AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State','AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement'),'runtime identity is Hotfix 4 or approved successor')
ck('OPERATION HEALTH' in B,'Ubuntu build identifies Operation Health lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State + AERIS24 Black Reload Successor] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
