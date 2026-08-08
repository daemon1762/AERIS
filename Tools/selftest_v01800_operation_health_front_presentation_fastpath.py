#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in (ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text(),'authoritative ND contract remains 10 Hz')
ck('CelestialBody frontBodyReference;' in R,'FRONT captures body object identity')
swap=R[R.index('void SwapFrontAndBack('):R.index('bool IsFrontBufferCompatible(')]
ck('frontBodyReference = vessel == null ? null : vessel.mainBody;' in swap,'body identity is captured only on authoritative FRONT swap')
fast=R[R.index('bool TryPresentCoalescedFront('):R.index('void MarkGpuContentDirty(')]
ck('ReferenceEquals(frontBodyReference, vessel.mainBody)' in fast,'non-authoritative path uses constant-time body identity check')
ck('string.Equals' not in fast and 'Math.Round' not in fast,'non-authoritative path removes body string/radius recomputation')
ck('CapturePresentedProjection(' not in fast,'non-authoritative path does not recopy full projection snapshot')
ck('presentedProjection.Valid = true;' in fast and 'presentedProjection.Latched = true;' in fast,'non-authoritative path reuses committed projection state')
ck(fast.count('PresentFrontDirect(') == 1,'IMGUI continuity path performs exactly one unavoidable retained-FRONT blit')
present=R[R.index('void PresentFrontDirect('):R.index('bool TryPresentReprojectedFront(')]
ck('FrontUvFlipped' in present and 'FrontUvDirect' in present and 'new Rect' not in present,'FRONT UV rectangles are cached')
reset=R[R.index('void ResetFrontBufferState('):R.index('void Schedule(')]
ck('frontBodyReference = null;' in reset,'full FRONT reset clears cached body identity')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'authoritative cadence remains fixed at 0.10 seconds')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'visual quality authority remains ARGB32 Bilinear')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health FRONT Presentation Fast Path] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
