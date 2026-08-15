#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
U = (ROOT / "build_ubuntu.sh").read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)


ck(('internal const string Revision = "OH_PHASE4_003";' in M) or
   ('internal const string Revision = "OH_PHASE4_004";' in M) or
   ('internal const string Revision = "OH_PHASE4_005";' in M) or
   ('internal const string Revision = "OH_PHASE4_006";' in M) or
   ('internal const string Revision = "OH_PHASE4_007";' in M),
   'ATROPINE revision is rev003 or approved rev004/rev005/rev006/rev007 descendant')
ck('AERIS25_CHUNK_CULL_GUARD' in R and
   'TileMayIntersectPresentation(tile, projection)' in R,
   'dot-cap cull candidates receive projected presentation witness')
ck('const float safetyMargin = 0.06f;' in R and
   'for (int row = 0; row < 3; row++)' in R and
   'for (int column = 0; column < 3; column++)' in R,
   'cull witness uses conservative 3x3 tile samples plus margin')
ck('projection.ProjectLatitudeLongitudeToGui(latitudeDeg,' in R,
   'cull witness uses the canonical Track-Up aware projection')
ck('return true;' in R[R.index('        bool TileMayIntersectPresentation'):R.index('        bool ShouldCullEntryOutsidePresentation')],
   'invalid cull-witness projection fails open toward drawing')
ck('operationHealthCulledEntries = Math.Max(0L,' in R and
   'operationHealthCullGuardVetoes++' in R and
   'operationHealthCullGuardConfirmed++' in R,
   'veto restores visible/cull accounting while confirmed candidates still skip')
ck('oh_cull_guard_veto=' in R and 'oh_cull_guard_confirm=' in R,
   'runtime publishes cull-guard veto and confirmation telemetry')
ck('ShouldCullEntryOutsidePresentation(drawEntry,' in R and
   'operationHealthDotCapCullTests++' in R,
   'accepted dot-cap remains the broad phase')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck(('REV003 CHUNK CULL GUARD' in U) or
   ('REV004 TEMPORAL FOUNDATION OVERSCAN' in U) or
   ('REV005 FOUNDATION CULL BYPASS' in U) or
   ('REV006 RENDERABLE ENTRY GATE' in U) or
   ('REV007 GPU VERTEX REJECT DIAGNOSTICS' in U),
   'build identity exposes rev003 guard or approved rev004/rev005/rev006/rev007 descendant')

frozen = ['Source/AERISFlightControl/AA', 'Source/AERISFlightControl/Autopilot',
          'Source/AERISFlightControl/Protect', 'Source/AERISFlightControl/Landing']
try:
    changed = subprocess.check_output(
        ['git', '-C', str(ROOT), 'diff', '--name-only', 'HEAD', '--'] + frozen,
        text=True).strip().splitlines()
except Exception:
    changed = ['GIT_DIFF_UNAVAILABLE']
ck(changed == [], 'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed = [name for ok, name in checks if not ok]
print("\n[AERIS25 ATROPINE REV003 CHUNK CULL GUARD] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV003 CHUNK CULL GUARD] STATIC PASS')
