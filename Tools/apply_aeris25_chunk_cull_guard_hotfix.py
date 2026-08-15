#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
RENDERER = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
MONITOR = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
BUILD = ROOT / "build_ubuntu.sh"


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS25 ATROPINE REV003] %s anchor mismatch old=%d" %
                         (label, count))
    return text.replace(old, new, 1), True


renderer = RENDERER.read_text()

field_old = '''        long operationHealthCullTests;
        long operationHealthCulledEntries;
        long operationHealthVisibleEntries;
        long operationHealthWideRangeCullBypassFrames;
        long operationHealthDotCapCullTests;
'''
field_new = '''        long operationHealthCullTests;
        long operationHealthCulledEntries;
        long operationHealthVisibleEntries;
        long operationHealthWideRangeCullBypassFrames;
        long operationHealthDotCapCullTests;
        long operationHealthCullGuardVetoes;
        long operationHealthCullGuardConfirmed;
'''
renderer, changed1 = replace_once(renderer, field_old, field_new,
                                  'cull guard telemetry fields')

cull_old = '''                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos)) continue;
'''
cull_new = '''                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos))
                    {
                        // AERIS25_CHUNK_CULL_GUARD: dot-cap remains the cheap broad phase,
                        // but runtime evidence showed complete FAR Entries could still be
                        // omitted as rectangular holes while foundation/coverage stayed
                        // READY. Only candidates already rejected by dot-cap pay for this
                        // 3x3 presentation witness. Any possible viewport intersection
                        // vetoes the cull; uncertainty therefore costs work, never pixels.
                        if (TileMayIntersectPresentation(tile, projection))
                        {
                            operationHealthCulledEntries = Math.Max(0L,
                                operationHealthCulledEntries - 1L);
                            operationHealthVisibleEntries++;
                            operationHealthCullGuardVetoes++;
                        }
                        else
                        {
                            operationHealthCullGuardConfirmed++;
                            continue;
                        }
                    }
'''
renderer, changed2 = replace_once(renderer, cull_old, cull_new,
                                  'dot-cap candidate safety guard')

helper_anchor = '''        bool ShouldCullEntryOutsidePresentation(Entry entry,
            double centerX, double centerY, double centerZ,
            double viewportRadiusSin, double viewportRadiusCos)
'''
helper = '''        bool TileMayIntersectPresentation(AERISTerrainHeightTile tile,
            AERISNdMapProjection projection)
        {
            if (tile == null) return true;
            const float safetyMargin = 0.06f;
            float minU = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float minV = float.PositiveInfinity;
            float maxV = float.NegativeInfinity;
            double latitudeSpan = tile.NorthLatitudeDeg - tile.SouthLatitudeDeg;
            double longitudeSpan = NormalizeLongitudeDelta(
                tile.EastLongitudeDeg - tile.WestLongitudeDeg);
            for (int row = 0; row < 3; row++)
            {
                double fy = row * 0.5;
                double latitudeDeg = tile.SouthLatitudeDeg + latitudeSpan * fy;
                for (int column = 0; column < 3; column++)
                {
                    double fx = column * 0.5;
                    double longitudeDeg = NormalizeLongitudeDegrees(
                        tile.WestLongitudeDeg + longitudeSpan * fx);
                    float u, v;
                    projection.ProjectLatitudeLongitudeToGui(latitudeDeg,
                        longitudeDeg, out u, out v);
                    if (float.IsNaN(u) || float.IsInfinity(u) ||
                        float.IsNaN(v) || float.IsInfinity(v)) return true;
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                }
            }
            // Fail open toward drawing. The guard is deliberately conservative:
            // a projected witness box near the display is sufficient to keep the Entry.
            return maxU >= -safetyMargin && minU <= 1f + safetyMargin &&
                maxV >= -safetyMargin && minV <= 1f + safetyMargin;
        }

'''
if helper not in renderer:
    if renderer.count(helper_anchor) != 1:
        raise SystemExit('[AERIS25 ATROPINE REV003] cull helper anchor mismatch')
    renderer = renderer.replace(helper_anchor, helper + helper_anchor, 1)
    changed3 = True
else:
    changed3 = False

telemetry_old = '''                "; oh_cull_wide_bypass=" + operationHealthWideRangeCullBypassFrames +
                "; oh_dot_cap_test=" + operationHealthDotCapCullTests +
                "; oh_mesh_pool=" + meshPool.Count +
'''
telemetry_new = '''                "; oh_cull_wide_bypass=" + operationHealthWideRangeCullBypassFrames +
                "; oh_dot_cap_test=" + operationHealthDotCapCullTests +
                "; oh_cull_guard_veto=" + operationHealthCullGuardVetoes +
                "; oh_cull_guard_confirm=" + operationHealthCullGuardConfirmed +
                "; oh_mesh_pool=" + meshPool.Count +
'''
renderer, changed4 = replace_once(renderer, telemetry_old, telemetry_new,
                                  'cull guard telemetry publication')

if any((changed1, changed2, changed3, changed4)):
    RENDERER.write_text(renderer)
    print('[AERIS25 ATROPINE REV003] presentation cull safety guard applied')
else:
    print('[AERIS25 ATROPINE REV003] presentation cull safety guard already present')

monitor = MONITOR.read_text()
if 'internal const string Revision = "OH_PHASE4_003";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_002";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV003] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_002";',
                              'internal const string Revision = "OH_PHASE4_003";', 1)
    MONITOR.write_text(monitor)
    print('[AERIS25 ATROPINE REV003] revision=OH_PHASE4_003')
else:
    print('[AERIS25 ATROPINE REV003] revision already OH_PHASE4_003')

build = BUILD.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV003 CHUNK CULL GUARD"'
build, display_changed = replace_once(build, old_display, new_display,
                                      'in-game display revision')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV003 CHUNK CULL GUARD";'
build, checkpoint_changed = replace_once(build, old_checkpoint, new_checkpoint,
                                         'in-game checkpoint revision')

ready_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py"'
cull_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_chunk_cull_guard_hotfix.py"'
active_ready = sum(1 for line in build.splitlines() if line.strip() == ready_verify)
active_cull = sum(1 for line in build.splitlines() if line.strip() == cull_verify)
verify_changed = False
if active_ready == 1 and active_cull == 0:
    build = build.replace(ready_verify, ready_verify + "\n" + cull_verify, 1)
    verify_changed = True
elif active_ready == 1 and active_cull == 1:
    pass
else:
    raise SystemExit('[AERIS25 ATROPINE REV003] build verifier gate mismatch ready=%d cull=%d' %
                     (active_ready, active_cull))

if display_changed or checkpoint_changed or verify_changed:
    BUILD.write_text(build)
    print('[AERIS25 ATROPINE REV003] build/in-game identity and verifier gate promoted')
else:
    print('[AERIS25 ATROPINE REV003] build/in-game identity and verifier gate already promoted')

print('[AERIS25 ATROPINE REV003] CHUNK CULL GUARD HOTFIX APPLIED')
print('Authority: dot-cap broad phase + fail-open projected 3x3 witness on cull candidates only')
print('No shader change; accepted AERIS25 AssetBundle may be reused')
