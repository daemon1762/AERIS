#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,'ND authoritative contract remains 10 Hz')
draw=R[R.index('internal AERISTerrainGpuDrawState Draw('):R.index('bool NeedsContentRefresh(')]
presentation=draw.index('float presentationNow')
gate=draw.index('TryPresentCoalescedFront(plot, vessel)')
settings=draw.index('AERISTerrainGpuMode currentGpuMode')
first_cache=draw.index('residentCache = system.CurrentBodyResidentCache;')
legacy_retained_gate=(presentation < gate < first_cache < settings)
accepted_staged_pump=(
    presentation < first_cache < gate < settings and
    'if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||' in draw[presentation:gate] and
    'rev35R019VisibleFoundationQueue.Count > 0 ||' in draw[presentation:gate] and
    'rev35R007FoundationQueue.Count > 0)' in draw[presentation:gate] and
    'PumpStagedCompletedCommit(system, false);' in draw[presentation:gate]
)
ck(legacy_retained_gate or accepted_staged_pump,
   'retained FRONT gate exists before normal renderer work under legacy or accepted staged-pump descendant')
ck(legacy_retained_gate or accepted_staged_pump,
   'retained gate precedes normal settings/GPU work; only accepted bounded staged pump may access resident cache first')
ck(gate < settings,'retained gate precedes settings/GPU-mode work')
pre_gate=draw[presentation:gate]
ck('CaptureVisible' not in pre_gate and 'DrainCompleted' not in pre_gate and
   'EnsureResources' not in pre_gate and 'ResolveRenderableEntries' not in pre_gate and
   'Schedule(tile' not in pre_gate,
   'retained pre-gate path performs no content capture/resource rebuild/new scheduling work')
if accepted_staged_pump:
 ck(pre_gate.count('PumpStagedCompletedCommit(system, false);') == 1,
    'accepted non-authoritative pre-gate work is exactly one staged pump call')
else:
 ck('PumpStagedCompletedCommit(system, false);' not in pre_gate,
    'legacy retained path has no staged pump before gate')
coalesced=R[R.index('bool TryPresentCoalescedFront('):R.index('void MarkGpuContentDirty(')]
ck(coalesced.count('PresentFrontDirect(')==1,'retained path has exactly one unavoidable IMGUI blit')
ck('CapturePresentedProjection(' not in coalesced,'retained path does not rebuild projection snapshot')
ck('operationHealthRetainedSurfaceBlits++;' in coalesced,'retained blits are independently observable')
ck('operationHealthCoalescedPresentFrames++;' not in coalesced,'legacy coalesced-present counter no longer advances at game FPS')
ck('if (present) operationHealthAuthoritativePresents++;' in R,'authoritative presentation count advances only on 10 Hz path')
ck('oh_auth_present=' in R and 'oh_retained_blit=' in R,'retained/authoritative telemetry is published')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'authoritative cadence remains 0.10 seconds')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'visual RenderTexture authority remains unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Retained FRONT Surface] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
