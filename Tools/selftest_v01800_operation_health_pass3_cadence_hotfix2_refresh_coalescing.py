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
def ck(v,n):
 checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
start=R.index('internal AERISTerrainGpuDrawState Draw(')
auth=R.index('operationHealthAuthoritativeTicks++',start)
non_tick=R[start:auth]
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S, 'fixed ND authority remains 10 Hz')
ck('nextAuthoritativePresentationTickRealtime' in R, 'independent authoritative presentation tick exists')
ck('presentationNow >= nextAuthoritativePresentationTickRealtime' in non_tick, 'authoritative tick gate exists before terrain work')
ck('TryPresentCoalescedFront(plot, vessel)' in non_tick, 'intervening Repaints use committed FRONT fast path')
ck('operationHealthCoalescedBlankPolls++' in non_tick, 'bootstrap blank polling is also cadence-limited')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R, 'authoritative tick advances by 0.10 seconds without catch-up burst')
ck('CaptureVisible' not in non_tick and 'DrainCompleted' not in non_tick, 'non-tick fast path performs no visible capture or worker drain')
ck(R.count('nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime') >= 2, 'BACK render shares authoritative tick deadline')
ck(R.count('gpuContentRevision++;') == 1 and 'void MarkGpuContentDirty()' in R, 'tile completion revisions are coalesced through one dirty helper')
ck('operationHealthDirtySignalsCoalesced++' in R and 'gpuContentDirty = false' in R[R.index('void SwapFrontAndBack'):], 'multiple tile completions collapse into one commit batch')
ck('rasterizer.PendingCount' in R[R.index('internal void InvalidatePendingForViewChange'):R.index('internal void SuspendViewport')], 'obsolete range/view worker count is captured before cancellation')
ck('scheduledThisFrame.Clear()' in R[R.index('internal void InvalidatePendingForViewChange'):R.index('internal void SuspendViewport')], 'view invalidation clears transient scheduler suppression state')
ck('nextAuthoritativePresentationTickRealtime = 0f' in R[R.index('void ResetFrontBufferState'):], 'true FRONT lifecycle reset re-arms immediate bootstrap')
ck('oh_auth_tick=' in R and 'oh_coalesced_present=' in R and 'oh_dirty_coalesced=' in R and 'oh_obsolete_cancel=' in R, 'runtime coalescing telemetry is published')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R, 'visual RenderTexture authority is unchanged')
ck('MaximumContourLevelsPerTile = 96' in (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(), 'Candidate11 contour authority remains 96 levels')
ck(V.get('NAME') in ('AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing', 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 3 Motion Commit', 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State', 'AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement'), 'runtime identity is Hotfix 2 or approved successor')
ck('OPERATION HEALTH' in B, 'Ubuntu build entrypoint identifies Operation Health lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
