#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
U = (ROOT / "build_ubuntu.sh").read_text()
SH = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)


ck('internal const string Revision = "OH_PHASE4_005";' in M,
   'ATROPINE revision is OH_PHASE4_005')
ck('AERIS25_FOUNDATION_CULL_BYPASS' in R,
   'renderer carries explicit foundation-cull bypass marker')
ck('tile.Key.Lod == AERISTerrainTileLod.Global ||' in R and
   'tile.Key.Lod == AERISTerrainTileLod.Far' in R,
   'Global and FAR are explicitly recognized as viewport foundation')
ck('if (entryCullingEnabled && foundationEntry)' in R and
   'operationHealthFoundationCullBypass++' in R,
   'foundation entries bypass whole-Entry cull when culling is enabled')
ck('else if (entryCullingEnabled &&\n                        ShouldCullEntryOutsidePresentation(drawEntry,' in R,
   'non-foundation entries still use the accepted dot-cap broad phase')
ck('TileMayIntersectPresentation(tile, projection)' in R and
   'operationHealthCullGuardVetoes++' in R and
   'operationHealthCullGuardConfirmed++' in R,
   'rev003 projected witness remains active for detail cull candidates')
ck('oh_foundation_cull_bypass=' in R,
   'runtime publishes foundation cull bypass telemetry')
ck('AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R and
   'oh_content_plan_range=' in R,
   'rev004 bounded temporal foundation overscan remains intact')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_FOUNDATION_CULL_BYPASS' not in SH,
   'rev005 is C# presentation-only and does not alter accepted shader equations')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage diagnostics remain present')
ck('REV005 FOUNDATION CULL BYPASS' in U and
   'verify_aeris25_foundation_cull_bypass_hotfix.py' in U,
   'build and in-game identity expose rev005 and enforce its verifier')

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
print("\n[AERIS25 ATROPINE REV005 FOUNDATION CULL BYPASS] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV005 FOUNDATION CULL BYPASS] STATIC PASS')
