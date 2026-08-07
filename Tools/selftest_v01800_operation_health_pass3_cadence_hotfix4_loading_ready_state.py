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
def flat(s): return ''.join(s.split())
ck('boolrequestedViewReady;' in flat(R) and 'internalboolRequestedViewReady' in flat(R),'requested-view READY is independent renderer state')
inv=R[R.index('internal void InvalidatePendingForViewChange'):R.index('internal void SuspendViewport')]
ck('requestedViewReady=false;' in flat(inv) and 'lastDrawState=AERISTerrainGpuDrawState.Partial;' in flat(inv),'view invalidation immediately becomes BUILDING/Partial')
ck('lastBackFoundationCoverage=0f;' in flat(inv) and 'lastCoverageFraction=0f;' in flat(inv),'new-view loading progress resets instead of inheriting stale 99 percent')
ck('ReleaseGpuResources' not in inv and 'ResetFrontBufferState' not in inv,'view invalidation retains old FRONT as continuity backdrop')
swap=R[R.index('void SwapFrontAndBack('):R.index('bool IsFrontBufferCompatible(')]
ck('requestedViewReady=true;' in flat(swap) and 'operationHealthRequestedViewReadyTransitions++' in swap,'only exact FRONT swap transitions requested view to READY')
fast=R[R.index('bool TryPresentCoalescedFront('):R.index('void MarkGpuContentDirty(')]
ck('requestedViewReady=true' not in flat(fast) and 'operationHealthLoadingBackdropFrames++' in fast,'cheap non-tick FRONT reuse cannot falsely declare READY')
end=R[R.index('UpdateReadyBuildingWatchdog(present'):R.index('bool NeedsContentRefresh(')]
ck('present&&requestedViewReady' in flat(end) and 'AERISTerrainGpuDrawState.Partial' in end,'visible stale FRONT reports Partial until requested view is READY')
reset=R[R.index('void ResetFrontBufferState()'):R.index('void Schedule(')]
ck('requestedViewReady=false;' in flat(reset),'true FRONT lifecycle reset clears requested-view READY')
ck('oh_loading_backdrop=' in R and 'oh_ready_transition=' in R and 'requested_view_ready=' in R,'runtime loading/readiness telemetry is published')
ck('if (gpuState == AERISTerrainGpuDrawState.Partial)' in N and 'TERRAIN GPU BUILDING ' in N,'ND UI renders BUILDING for renderer Partial state')
ck('terrainTileRenderer.InvalidatePendingForViewChange();' in N and 'RangeChangeDebounceSeconds = 0.35f' in N,'coalesced range changes enter requested-view invalidation path')
ck('returnTime.realtimeSinceStartup-frontCommittedRealtime<=8.0f;' in flat(R),'existing stale-FRONT continuity safety window remains unchanged')
ck('nextAuthoritativePresentationTickRealtime=presentationNow+0.10f' in flat(R),'10 Hz authoritative cadence remains unchanged')
ck('AuthoritativeMotionSpeedMetersPerSecond=0.5f' in flat(R) and 'forceCenterProjectionRefresh' in R,'Hotfix 3 exact moving-map 10 Hz path remains intact')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target visual quality authority unchanged')
RA=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
ck('MaximumContourLevelsPerTile = 96' in RA and 'MaximumSparseCorrectionParentCells = 256' in RA,'Candidate11 contour/coastal authorities unchanged')
ck(V.get('NAME') in (
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State',
 'AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement',
 'AERISFlightControl DEV CP3.75 Operation Health Step 3 Worker Projection'),
 'runtime identity is Hotfix 4 or approved successor')
ck('OPERATION HEALTH' in B,'Ubuntu build identifies Operation Health lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
