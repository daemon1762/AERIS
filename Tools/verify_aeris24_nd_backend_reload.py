#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
S = (ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs").read_text()
B = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
N = (ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs").read_text()

checks = []
def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)

ck('internal enum AERISNdProjectionBackendMode' in S and
   'Automatic = 0' in S and 'Cpu = 1' in S and 'Gpu = 2' in S,
   'AUTO/CPU/GPU projection backend enum exists')
ck('NavigationDisplayProjectionBackend' in S and
   'navigationDisplayProjectionBackend' in S,
   'projection backend preference is persisted')
ck('case AERISNdProjectionBackendMode.Cpu: return "CPU";' in B and
   'case AERISNdProjectionBackendMode.Gpu: return "GPU";' in B and
   'default: return "AUTO";' in B,
   'requested backend telemetry uses AUTO/CPU/GPU names')

ensure_start = B.index('internal bool TryEnsureLoaded()')
ensure_end = B.index('internal void ConfigureProjection(', ensure_start)
ensure = B[ensure_start:ensure_end]
cpu_gate = ensure.index('if (requestedMode == AERISNdProjectionBackendMode.Cpu)')
probe_call = ensure.index('RunContainerProbe(')
ck(cpu_gate < probe_call and 'AssetBundleInit=0' in ensure and
   'failure = "CPU_EXACT_REQUESTED";' in ensure,
   'explicit CPU hard-gates all bundle/probe initialization')
ck('RequestedModeName' in B and 'EffectiveModeName' in B and
   'CPU_EXACT' in B and 'GPU_ACTIVE' in B and 'CPU_FALLBACK' in B,
   'requested/effective backend states are separately observable')
ck('ReleaseForSuspension();\n            requestedMode = mode;' in B,
   'backend mode switch resets prior GPU attempt/resource state')

ck('internal bool Reloading' in R and
   'frontReloadGeneration != ndReloadGeneration' in R,
   'reload state is generation-gated')
ck('ReloadProgressPercent' in R and 'lastBackFoundationCoverage' in R and
   'Mathf.Clamp' in R and ', 0, 99)' in R,
   'reload percent derives from real foundation coverage and cannot fake 100 percent')
inv = R[R.index('internal void InvalidatePendingForViewChange'):
        R.index('internal void SuspendViewport')]
ck('ndReloadGeneration++;' in inv and 'requestedViewReady = false;' in inv and
   'lastBackFoundationCoverage = 0f;' in inv,
   'explicit view invalidation starts a fresh black reload generation at zero progress')
ck('ReleaseGpuResources' not in inv and 'ResetFrontBufferState' not in inv,
   'reload keeps old FRONT resources available but not presentable')

swap = R[R.index('void SwapFrontAndBack('):R.index('bool IsFrontBufferCompatible(')]
ck(swap.index('frontReloadGeneration = ndReloadGeneration;') <
   swap.index('requestedViewReady = true;'),
   'only a fresh FRONT swap atomically closes the reload generation')
fast = R[R.index('bool TryPresentCoalescedFront('):R.index('void MarkGpuContentDirty(')]
ck('if (Reloading) return false;' in fast,
   'coalesced stale FRONT is hard-blocked while reload is active')
ck('if (!Reloading && directCompatible)' in R and
   'if (!present && !Reloading && colourCompatible' in R,
   'direct/latched stale FRONT presentation is hard-blocked during reload')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'black reload does not alter fixed 10 Hz authoritative cadence')
ck('CanPresentLatchedFront' in R and
   'return Time.realtimeSinceStartup - frontCommittedRealtime <= 8.0f;' in R,
   'non-reload stale-FRONT safety window remains available')

ck('oh_gpu_vertex_requested=' in R and
   'oh_gpu_vertex_projection=' in R and
   'oh_nd_reload=' in R and 'oh_nd_reload_pct=' in R,
   'OH publishes requested/effective backend plus black-reload telemetry')
ck('RELOADING ND\\n' in N and 'terrainTileRenderer.ReloadProgressPercent' in N,
   'ND renders centered RELOADING ND with actual percent')
ck('terrainTileRenderer.LastDrawState == AERISTerrainGpuDrawState.Partial' in N,
   'black reload UI is limited to an actual rebuilding terrain presentation')
black_gate = N[N.index('bool ndReloading ='):N.index('// Gate 5 Candidate 2 map-authority latch.')]
ck('DrawCleanBackground(plan);' in black_gate and
   'DrawCleanBackground(profile);' in black_gate and
   'DrawMapControls(' in black_gate and 'return;' in black_gate,
   'reload hides map/profile/symbology while preserving bezel controls')
ck('FormatProjectionBackend' in N and 'PROJ AUTO' in N and
   'PROJ CPU' in N and 'PROJ GPU' in N and 'CycleProjectionBackend' in N,
   'ND MENU exposes AUTO/CPU/GPU selector')
ck('RangeChangeDebounceSeconds = 0.35f' in N and
   'terrainTileRenderer.InvalidatePendingForViewChange();' in N,
   'coalesced range reload enters the black-reload rail')
cycle_mode = N[N.index('void CycleTerrainMode()'):N.index('void DrawMapControls(')]
ck('terrainTileRenderer.InvalidatePendingForViewChange();' in cycle_mode,
   'terrain mode changes explicitly enter black reload')
controls = N[N.index('void DrawMapControls('):N.index('static Rect ResolveAuxiliaryMenuRect(')]
ck('settings.NavigationDisplayTrackUp = !settings.NavigationDisplayTrackUp;' in controls and
   'terrainTileRenderer.InvalidatePendingForViewChange();' in controls,
   'TRACK/NORTH discrete view change explicitly enters black reload')
orientation = N[N.index('void CycleTerrainRenderTargetOrientation()'):N.index('void CycleTerrainMode()')]
ck('terrainTileRenderer.ResetGpuFailure();' in orientation and
   'terrainTileRenderer.InvalidatePendingForViewChange();' in orientation,
   'RT orientation change resets graphics failure and enters black reload')

A = (ROOT / 'Tools/apply_aeris24_nd_backend_reload.py').read_text()
for token in ('Source/AERISFlightControl/AA', 'Source/AERISFlightControl/Autopilot',
              'Source/AERISFlightControl/Protect', 'Source/AERISFlightControl/Landing'):
    ck(token not in A, 'successor applicator does not target ' + token.split('/')[-1])
ck('new Thread' not in A and 'Task.Run' not in A and 'ThreadPool' not in A,
   'successor adds no asynchronous control/runtime authority')

failed = [name for ok, name in checks if not ok]
print("\n[AERIS24 ND BACKEND/BLACK RELOAD] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS24 ND BACKEND/BLACK RELOAD] SOURCE+SAFETY PASS')
