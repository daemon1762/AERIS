#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
S = (ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs").read_text()
W = (ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs").read_text()
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
B = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
C = (ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg").read_text()

checks = []
def ck(ok, label):
    checks.append((bool(ok), label))
    print(("[PASS] " if ok else "[FAIL] ") + label)

ck('DrawProjectionBackendSelector();' in W and '"ND projection"' in W and
   'new string[]{"AUTO","CPU","GPU"}' in W,
   "SYSTEM > OPTIONS exposes the same AUTO/CPU/GPU projection setting")
ck('settings.NavigationDisplayProjectionBackend=next;' in W,
   "SYSTEM projection selector writes the shared persisted backend setting")
ck('DrawTerrainGpuSelector' not in W and '"Terrain GPU"' not in W,
   "legacy Terrain GPU selector is removed from SYSTEM > OPTIONS")
ck('FlightArchiveLimitLabels' not in W and 'GUILayout.HorizontalSlider(current,1f,30f' in W,
   "FDR/CVR retention uses one 1..30 slider instead of 30 buttons")
ck('Mathf.RoundToInt(raw)' in W and 'ConfigureRetention(next)' in W,
   "FDR/CVR slider snaps to integer retention and preserves runtime retention update")

ck('internal AERISTerrainGpuMode TerrainGpuMode = AERISTerrainGpuMode.On;' in S,
   "Terrain GPU default is fixed ON")
ck('settings.TerrainGpuMode = AERISTerrainGpuMode.On;' in S and
   'ReadEnum(node, "terrainGpuMode"' not in S,
   "legacy saved Terrain GPU AUTO/OFF values are normalized to ON")
ck('TerrainGpuMode = AERISTerrainGpuMode.On;' in S and
   'node.AddValue("terrainGpuMode", TerrainGpuMode);' in S,
   "factory reset and save retain Terrain GPU ON policy")
ck('terrainGpuMode = On' in C and 'terrainGpuMode = Automatic' not in C,
   "packaged settings advertise Terrain GPU ON")

ck('gpuVertexProjection.RetainForViewportSuspension();' in R and
   'gpuVertexProjection.ReleaseForSuspension();' not in R,
   "viewport suspension keeps AssetBundle/shader/material backend resident")
ck('internal void RetainForViewportSuspension()' in B and
   'viewportSuspendedResident' in B,
   "backend has idempotent resident-suspension state")
resident_start = B.find('internal void RetainForViewportSuspension()')
resident_end = B.find('internal void DisableAndFallback', resident_start)
resident = B[resident_start:resident_end] if resident_start >= 0 and resident_end > resident_start else ''
ck('Unload(' not in resident and 'DestroyMaterial' not in resident and
   'AssetBundle.LoadFromFile' not in resident,
   "visibility suspension performs no bundle unload/reload or material destruction")
ck('internal void SetRequestedMode' in B and 'ReleaseForSuspension();' in B,
   "explicit CPU/GPU backend switch still owns a true resource reset")
ck('public void Dispose()' in B and 'ReleaseForSuspension();' in B,
   "flight/subsystem disposal still fully releases GPU projection resources")
ck('activationCount++' in B and 'oh_gpu_vertex_activation=' in R and
   'oh_gpu_vertex_resident_suspend=' in R,
   "runtime telemetry distinguishes real activations from resident visibility suspends")

ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   "fixed 10 Hz ND authoritative cadence remains unchanged")
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   "Golden render-target format remains unchanged")
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   "Runway Map Lock and Golden visual coverage diagnostics remain present")
ck('oh_nd_reload_snapshot=' in R and 'reloadSnapshotCenterLatitudeDeg' in R,
   "rev005 frozen reload snapshot remains intact")

frozen = [
    'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect',
    'Source/AERISFlightControl/Landing',
]
try:
    changed = subprocess.check_output(
        ['git', '-C', str(ROOT), 'diff', '--name-only', 'HEAD', '--'] + frozen,
        text=True).strip().splitlines()
except Exception:
    changed = ['GIT_DIFF_UNAVAILABLE']
ck(changed == [], "AA/AP/PROTECT/LAND working-tree edits remain NONE")

failed = [label for ok, label in checks if not ok]
print("\n[AERIS24 SYSTEM OPTIONS + GPU RESIDENCY] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    raise SystemExit("; ".join(failed))
print("[AERIS24 SYSTEM OPTIONS + GPU RESIDENCY] STATIC PASS")
