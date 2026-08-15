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


ck('internal const string Revision = "OH_PHASE4_006";' in M,
   'ATROPINE revision is OH_PHASE4_006')
ck('AERIS25_RENDERABLE_ENTRY_GATE' in R,
   'renderer carries explicit renderable-entry gate marker')
ck('operationHealthFoundationCullBypass++' not in R and
   'ShouldCullEntryOutsidePresentation(drawEntry,' in R and
   'TileMayIntersectPresentation(tile, projection)' in R,
   'rejected rev005 foundation bypass is removed and rev003 culling is restored')
ck('if (!HasRenderableTerrain(entry))' in R and
   'operationHealthNonRenderableEntryRejects++' in R and
   'RemoveRenderReadyField(cacheKey, field);' in R,
   'cached render-ready field cannot promote a non-renderable Entry')
ck('RemoveRenderReadyField(cacheKey, result);' in R and
   'CaptureAndMarkRenderReady(result, system);' in R,
   'fresh worker result must prove renderability before render-ready authority is published')
ck('result.Triangles.Length < 3' in R and
   'operationHealthEmptyTriangleResults++' in R,
   'zero-triangle worker results are rejected before Entry promotion')
ck('bool currentRenderable = HasRenderableTerrain(currentEntry);' in R and
   'currentEntriesScratch[i] = currentRenderable ? currentEntry : null;' in R and
   'drawEntriesScratch[i] = currentRenderable ? currentEntry : fallbackEntry;' in R,
   'non-renderable current Entry cannot shadow drawable fallback')
ck('operationHealthFallbackShadowPrevents++' in R,
   'fallback-shadow prevention is directly observable')
ck('if (!HasRenderableTerrain(current) ||' in R and
   'current.CoverageFraction < 0.999f' in R,
   'foundation GPU readiness requires actual drawable terrain')
ck('oh_nonrenderable_entry_reject=' in R and
   'oh_fallback_shadow_prevent=' in R and
   'oh_empty_triangle_result=' in R,
   'runtime publishes focused renderability diagnostics')
ck('oh_foundation_cull_bypass=' in R,
   'rev005 bypass counter remains visible as a zero-activity rollback witness')
ck('AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R and
   'oh_content_plan_range=' in R,
   'bounded rev004 hidden planning overscan remains intact for later evaluation')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_RENDERABLE_ENTRY_GATE' not in SH,
   'rev006 is C# presentation correctness only; accepted shader equations are unchanged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage diagnostics remain present')
ck('REV006 RENDERABLE ENTRY GATE' in U and
   'verify_aeris25_renderable_entry_gate_hotfix.py' in U and
   'verify_aeris25_foundation_cull_bypass_hotfix.py' not in
       '\n'.join(line for line in U.splitlines()
                 if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3')),
   'build identity and active verifier gate are rev006-specific')

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
print("\n[AERIS25 ATROPINE REV006 RENDERABLE ENTRY GATE] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV006 RENDERABLE ENTRY GATE] STATIC PASS')
