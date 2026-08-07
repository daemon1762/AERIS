#!/usr/bin/env python3
from pathlib import Path
import json,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
F=''.join(R.split())
W=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.WorkerProjection.cs').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
B=(ROOT/'build_ubuntu.sh').read_text()
V=json.loads((ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text())
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
start=R.index('internal AERISTerrainGpuDrawState Draw(')
auth=R.index('operationHealthAuthoritativeTicks++',start)
non_tick=R[start:auth]
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,'fixed ND authority remains 10 Hz')
ck('nextAuthoritativePresentationTickRealtime' in R,'independent authoritative presentation tick exists')
ck('presentationNow>=nextAuthoritativePresentationTickRealtime' in ''.join(non_tick.split()),'authoritative tick gate exists before terrain work')
ck('TryPresentCoalescedFront(plot,vessel)' in ''.join(non_tick.split()),'intervening Repaints use committed FRONT fast path')
ck('operationHealthCoalescedBlankPolls++' in non_tick,'bootstrap blank polling is cadence-limited')
ck('nextAuthoritativePresentationTickRealtime=presentationNow+0.10f' in F,'authoritative tick advances by 0.10 seconds without catch-up burst')
ck('CaptureVisible' not in non_tick and 'DrainCompleted' not in non_tick,'non-tick fast path performs no visible capture or tile-worker drain')
ck(F.count('nextBackRefreshRealtime=nextAuthoritativePresentationTickRealtime') >= 2,'BACK render shares authoritative tick deadline')
ck(F.count('gpuContentRevision++;') == 1 and 'voidMarkGpuContentDirty()' in F,'tile completion revisions are coalesced through one dirty helper')
swap=R[R.index('void SwapFrontAndBack'):]
ck('operationHealthDirtySignalsCoalesced++' in R and 'gpuContentDirty=false' in ''.join(swap.split()),'multiple tile completions collapse into one commit batch')
inv=R[R.index('internal void InvalidatePendingForViewChange'):R.index('internal void SuspendViewport')]
ck('rasterizer.PendingCount' in inv,'obsolete range/view worker count is captured before cancellation')
ck('scheduledThisFrame.Clear()' in inv,'view invalidation clears transient scheduler suppression state')
reset=R[R.index('void ResetFrontBufferState'):]
ck('nextAuthoritativePresentationTickRealtime=0f' in ''.join(reset.split()),'true FRONT lifecycle reset re-arms immediate bootstrap')
ck('oh_auth_tick=' in R and 'oh_coalesced_present=' in R and 'oh_dirty_coalesced=' in R and 'oh_obsolete_cancel=' in R,'runtime coalescing telemetry is published')
ck('ProjectionWorkerMinimumCommitIntervalSeconds = 0.10f' in W and
   'Time.realtimeSinceStartup - frontCommittedRealtime <' in W,
   'async Worker FRONT commit retains absolute 0.10 second minimum cadence')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'visual RenderTexture authority is unchanged')
ck('MaximumContourLevelsPerTile = 96' in (ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text(),'Candidate11 contour authority remains 96 levels')
ck(V.get('NAME') in (
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing',
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 3 Motion Commit',
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State',
 'AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement',
 'AERISFlightControl DEV CP3.75 Operation Health Step 3 Worker Projection'),
 'runtime identity is Hotfix 2 or approved successor')
ck('OPERATION HEALTH' in B,'Ubuntu build entrypoint identifies Operation Health lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
