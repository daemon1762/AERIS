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
start=R.index('bool ShouldRefreshBackBuffer(')
end=R.index('bool NeedsProjectionRefresh(',start)
m=R[start:end]
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S, 'ND presentation contract remains fixed at 10 Hz')
ck('Time.realtimeSinceStartup < nextBackRefreshRealtime' in m, 'ordinary BACK refresh has an absolute cadence gate')
ck('lastBackAttemptViewGeneration != visible.ViewGeneration) return true' not in m, 'view generation cannot bypass cadence gate')
ck('lastBackAttemptContentRevision != gpuContentRevision) return true' not in m, 'content revision cannot bypass cadence gate')
ck('operationHealthCadenceDeferrals++' in m, 'cadence deferrals are observable')
ck('!frontBufferValid && lastBackAttemptViewGeneration < 0L' in m and 'operationHealthCadenceBootstrapBypasses++' in m, 'first FRONT bootstrap remains immediate and observable')
ck(R.count('nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime') >= 2, 'normal and forced recovery renders preserve the shared 0.10 second scheduling authority')
ck('forcedRecoveryBackRenders++' in R, 'blank-recovery exception remains separate and observable')
ck('oh_cadence_defer=' in R and 'oh_cadence_bootstrap=' in R, 'runtime cadence telemetry is published')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R, 'render-target visual quality authority unchanged')
ck(V.get('NAME') in ('AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 1', 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing', 'AERISFlightControl DEV CP3.75 Operation Health Pass 3 Cadence Hotfix 3 Motion Commit'), 'runtime package identity is Cadence Hotfix 1 or approved successor')
ck('OPERATION HEALTH PASS 3 CADENCE HOTFIX' in B, 'Ubuntu build entrypoint identifies Cadence Hotfix lineage')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 3 Cadence Hotfix 1] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
