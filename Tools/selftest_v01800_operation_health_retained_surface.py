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
fast=draw[draw.index('float presentationNow'):draw.index('residentCache = system.CurrentBodyResidentCache;')]
ck('TryPresentCoalescedFront(plot, vessel)' in fast,'retained FRONT gate exists before normal renderer work')
ck(draw.index('TryPresentCoalescedFront(plot, vessel)') < draw.index('residentCache = system.CurrentBodyResidentCache;'),'retained gate precedes resident-cache access')
ck(draw.index('TryPresentCoalescedFront(plot, vessel)') < draw.index('AERISTerrainGpuMode currentGpuMode'),'retained gate precedes settings/GPU-mode work')
ck('CaptureVisible' not in fast and 'DrainCompleted' not in fast and 'EnsureResources' not in fast,'retained gate performs no content/resource work')
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
