#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
T = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs").read_text()
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
U = (ROOT / "build_ubuntu.sh").read_text()
SH = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)


ck(('internal const string Revision = "OH_PHASE4_004";' in M) or
   ('internal const string Revision = "OH_PHASE4_005";' in M),
   'ATROPINE revision is rev004 or approved rev005 descendant')
ck('AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R and
   'float historySurfaceRangeMeters = ResolveHistorySurfaceRange(rangeMeters);' in R,
   'hidden content footprint activates existing bounded temporal overscan')
ck('const float HistoryOverscanScale = 1.35f;' in R and
   'const float MaximumHistorySurfaceRangeMeters = 250000f;' in R,
   'overscan remains bounded to accepted 1.35x / 250 km authority')
ck('centerLongitudeDeg, historySurfaceRangeMeters, mapHeadingDeg, trackUp,' in R,
   'content CaptureVisible receives overscan planning range')
ck('ResolveEffectiveMode(requestedMode,\n                vessel, rangeMeters)' in R and
   'ResolveVirtualDetailProfile(rangeMeters)' in R and
   'ResolveContourInterval(rangeMeters)' in R,
   'display mode/detail/contour authority remains based on exact visible range')
ck('AERISNdMapProjection.Create(\n                vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg, rangeMeters,' in R,
   'canonical map projection remains exact user-visible range')
ck('ResolveViewportCullCap(vessel.mainBody,\n                    rangeMeters, anchorV' in R,
   'non-foundation whole-Entry culling remains tied to exact visible viewport rather than overscan')
ck('SwapFrontAndBack(visible, vessel, centerLatitudeDeg,\n                        centerLongitudeDeg, rangeMeters, rangeMeters,' in R,
   'FRONT projection/surface identity remains exact visible range')
ck('oh_content_visible_range=' in R and 'oh_content_plan_range=' in R and
   'oh_temporal_overscan_capture=' in R,
   'runtime directly exposes visible/planning range split and overscan captures')
ck('AERIS25_CHUNK_CULL_GUARD' in R and
   'oh_cull_guard_veto=' in R and 'oh_cull_guard_confirm=' in R,
   'rev003 false-cull guard remains intact')
ck('displayViewRangeMeters can be an internal temporal-overscan range' in T and
   'double normalizedRange = Math.Max(1000.0, Math.Min(250000.0, rangeMeters));' in T,
   'TileSystem explicitly preserves internal non-UI overscan range for planning')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' not in SH,
   'rev004 is C# planning-only and does not alter accepted shader equations')
ck((('REV004 TEMPORAL FOUNDATION OVERSCAN' in U) or
    ('REV005 FOUNDATION CULL BYPASS' in U)) and
   'verify_aeris25_temporal_foundation_overscan_hotfix.py' in U,
   'build identity exposes rev004 or approved rev005 descendant and enforces overscan verifier')

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
print("\n[AERIS25 ATROPINE REV004 TEMPORAL FOUNDATION OVERSCAN] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV004 TEMPORAL FOUNDATION OVERSCAN] STATIC PASS')
