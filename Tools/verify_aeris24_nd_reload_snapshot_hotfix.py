#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
checks = []

def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)

ck('bool reloadSnapshotPending = true;' in R and
   'bool reloadSnapshotActive;' in R and
   'reloadSnapshotCenterLatitudeDeg' in R and
   'reloadSnapshotCenterLongitudeDeg' in R and
   'reloadSnapshotMapHeadingDeg' in R,
   'reload snapshot state exists')

inv = R[R.index('internal void InvalidatePendingForViewChange'):
        R.index('internal void SuspendViewport')]
ck('ndReloadGeneration++;' in inv and
   'reloadSnapshotPending = true;' in inv and
   'reloadSnapshotActive = false;' in inv and
   'reloadProgressPercentFloor = 0;' in inv,
   'every discrete invalidation starts a fresh snapshot generation')

start = R.index('AERISNdProjectionBackendMode requestedProjectionBackend')
end = R.index('float presentationNow = Time.realtimeSinceStartup;', start)
freeze = R[start:end]
ck('if (Reloading)' in freeze and
   'reloadSnapshotCenterLatitudeDeg = centerLatitudeDeg;' in freeze and
   'reloadSnapshotCenterLongitudeDeg = centerLongitudeDeg;' in freeze and
   'reloadSnapshotMapHeadingDeg = mapHeadingDeg;' in freeze,
   'black reload captures one live motion snapshot')
ck('centerLatitudeDeg = reloadSnapshotCenterLatitudeDeg;' in freeze and
   'centerLongitudeDeg = reloadSnapshotCenterLongitudeDeg;' in freeze and
   'mapHeadingDeg = reloadSnapshotMapHeadingDeg;' in freeze,
   'black reload substitutes frozen center and heading before content/projection work')
ck(freeze.index('centerLatitudeDeg = reloadSnapshotCenterLatitudeDeg;') <
   freeze.index('operationHealthReloadSnapshotFrames++;'),
   'snapshot authority is applied before reload frame accounting')

prop_start = R.index('internal int ReloadProgressPercent')
prop_end = R.index('internal string ProjectionBackendRequested', prop_start)
prop = R[prop_start:prop_end]
ck('if (measured > reloadProgressPercentFloor)' in prop and
   'reloadProgressPercentFloor = measured;' in prop and
   'return reloadProgressPercentFloor;' in prop,
   'reload percentage is monotonic within one generation')

swap = R[R.index('void SwapFrontAndBack('):R.index('bool IsFrontBufferCompatible(')]
ck('frontReloadGeneration = ndReloadGeneration;' in swap and
   'requestedViewReady = true;' in swap and
   'reloadSnapshotActive = false;' in swap and
   'reloadSnapshotPending = false;' in swap,
   'only fresh FRONT commit releases the reload snapshot')

ck('oh_nd_reload_snapshot=' in R and
   'oh_nd_reload_snapshot_capture=' in R and
   'oh_nd_reload_snapshot_frames=' in R,
   'snapshot runtime telemetry is exported')
ck('[AERIS24_ND_RELOAD_SNAPSHOT]' in R,
   'snapshot capture event is explicitly logged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   '10 Hz authoritative cadence remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden render-target format remains unchanged')
ck('lastRunwayMapLockErrorPixels > 1.0f' in R and 'visualCoverage=' in R,
   'Runway Map Lock and visual coverage guards remain present')

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
ck(changed == [], 'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed = [name for ok, name in checks if not ok]
print('\n[AERIS24 ND RELOAD SNAPSHOT HOTFIX] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS24 ND RELOAD SNAPSHOT HOTFIX] STATIC PASS')
