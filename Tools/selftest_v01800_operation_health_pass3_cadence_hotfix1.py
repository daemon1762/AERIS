#!/usr/bin/env python3
from pathlib import Path
import json,sys
sys.dont_writebytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
F=''.join(R.split())
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
B=(ROOT/'build_ubuntu.sh').read_text()
V=json.loads((ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text())
checks=[]
def ck(v,n):
 checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
start=R.index('bool ShouldRefreshBackBuffer(')
end=R.index('bool NeedsAuthoritativeMotionRefresh(',start)
m=R[start:end]
MF=''.join(m.split())
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,'ND presentation contract remains fixed at 10 Hz')
ck('Time.realtimeSinceStartup<nextBackRefreshRealtime' in MF,'ordinary BACK refresh has an absolute cadence gate')
ck('lastBackAttemptViewGeneration!=visible.ViewGeneration)returntrue' not in MF,'view generation cannot bypass cadence gate')
ck('lastBackAttemptContentRevision!=gpuContentRevision)returntrue' not in MF,'content revision cannot bypass cadence gate')
ck('operationHealthCadenceDeferrals++' in m,'cadence deferrals are observable')
ck('!frontBufferValid&&lastBackAttemptViewGeneration<0L' in MF and 'operationHealthCadenceBootstrapBypasses++' in m,'first FRONT bootstrap remains immediate and observable')
ck(F.count('nextBackRefreshRealtime=nextAuthoritativePresentationTickRealtime') >= 2,'normal and forced recovery renders preserve shared 0.10 second scheduling authority')
ck('forcedRecoveryBackRenders++' in R,'blank-recovery exception remains separate and observable')
ck('oh_cadence_defer=' in R and 'oh_cadence_bootstrap=' in R,'runtime cadence telemetry is published')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'render-target visual quality authority unchanged')
ck(V.get('NAME') in (
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 1',
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing',
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 3 Motion Commit',
 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State',
 'AERISFlightControl DEV CP3.75 Operation Health Step 2 Motion Content Split Coastal Edge Refinement',
 'AERISFlightControl DEV CP3.75 Operation Health Step 3 Worker Projection'),
 'runtime package identity is Cadence Hotfix 1 or approved successor')
ck('OPERATION HEALTH' in B,'Ubuntu build entrypoint identifies Operation Health lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 1] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
