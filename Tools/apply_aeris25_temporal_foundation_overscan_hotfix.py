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
        raise SystemExit("[AERIS25 ATROPINE REV004] %s anchor mismatch old=%d" %
                         (label, count))
    return text.replace(old, new, 1), True


renderer = RENDERER.read_text()

field_old = '''        long operationHealthDotCapCullTests;
        long operationHealthCullGuardVetoes;
        long operationHealthCullGuardConfirmed;
        long useSequence;
'''
field_new = '''        long operationHealthDotCapCullTests;
        long operationHealthCullGuardVetoes;
        long operationHealthCullGuardConfirmed;
        float operationHealthContentVisibleRangeMeters;
        float operationHealthContentPlanningRangeMeters;
        long operationHealthTemporalOverscanCaptures;
        long useSequence;
'''
renderer, changed1 = replace_once(renderer, field_old, field_new,
                                  'temporal overscan telemetry fields')

range_old = '''            float historySurfaceRangeMeters = rangeMeters;
'''
range_new = '''            // AERIS25_TEMPORAL_FOUNDATION_OVERSCAN: user-visible projection remains
            // exactly rangeMeters. Only the hidden content/foundation request footprint is
            // widened so 10 Hz centre/Track-Up motion cannot outrun the last content plan at
            // the ND edge. The existing bounded 1.35x / 250 km authority is reused.
            float historySurfaceRangeMeters = ResolveHistorySurfaceRange(rangeMeters);
            operationHealthContentVisibleRangeMeters = rangeMeters;
            operationHealthContentPlanningRangeMeters = historySurfaceRangeMeters;
'''
renderer, changed2 = replace_once(renderer, range_old, range_new,
                                  'activate bounded foundation overscan range')

capture_old = '''                visible = system.CaptureVisible(centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
                operationHealthContentCaptures++;
'''
capture_new = '''                visible = system.CaptureVisible(centerLatitudeDeg,
                    centerLongitudeDeg, historySurfaceRangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
                operationHealthContentCaptures++;
                operationHealthTemporalOverscanCaptures++;
'''
renderer, changed3 = replace_once(renderer, capture_old, capture_new,
                                  'foundation capture uses hidden overscan footprint')

telemetry_old = '''                "; oh_content_capture=" + operationHealthContentCaptures +
                "; oh_content_drain=" + operationHealthContentWorkerDrains +
'''
telemetry_new = '''                "; oh_content_capture=" + operationHealthContentCaptures +
                "; oh_content_visible_range=" +
                    operationHealthContentVisibleRangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; oh_content_plan_range=" +
                    operationHealthContentPlanningRangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; oh_temporal_overscan_capture=" + operationHealthTemporalOverscanCaptures +
                "; oh_content_drain=" + operationHealthContentWorkerDrains +
'''
renderer, changed4 = replace_once(renderer, telemetry_old, telemetry_new,
                                  'temporal overscan runtime telemetry')

if any((changed1, changed2, changed3, changed4)):
    RENDERER.write_text(renderer)
    print('[AERIS25 ATROPINE REV004] temporal foundation overscan applied')
else:
    print('[AERIS25 ATROPINE REV004] temporal foundation overscan already present')

monitor = MONITOR.read_text()
if 'internal const string Revision = "OH_PHASE4_004";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_003";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV004] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_003";',
                              'internal const string Revision = "OH_PHASE4_004";', 1)
    MONITOR.write_text(monitor)
    print('[AERIS25 ATROPINE REV004] revision=OH_PHASE4_004')
else:
    print('[AERIS25 ATROPINE REV004] revision already OH_PHASE4_004')

build = BUILD.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV003 CHUNK CULL GUARD"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV004 TEMPORAL FOUNDATION OVERSCAN"'
build, display_changed = replace_once(build, old_display, new_display,
                                      'in-game display revision')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV003 CHUNK CULL GUARD";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV004 TEMPORAL FOUNDATION OVERSCAN";'
build, checkpoint_changed = replace_once(build, old_checkpoint, new_checkpoint,
                                         'in-game checkpoint revision')

cull_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_chunk_cull_guard_hotfix.py"'
overscan_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py"'
active_cull = sum(1 for line in build.splitlines() if line.strip() == cull_verify)
active_overscan = sum(1 for line in build.splitlines() if line.strip() == overscan_verify)
verify_changed = False
if active_cull == 1 and active_overscan == 0:
    build = build.replace(cull_verify, cull_verify + "\n" + overscan_verify, 1)
    verify_changed = True
elif active_cull == 1 and active_overscan == 1:
    pass
else:
    raise SystemExit('[AERIS25 ATROPINE REV004] build verifier gate mismatch cull=%d overscan=%d' %
                     (active_cull, active_overscan))

if display_changed or checkpoint_changed or verify_changed:
    BUILD.write_text(build)
    print('[AERIS25 ATROPINE REV004] build/in-game identity and verifier gate promoted')
else:
    print('[AERIS25 ATROPINE REV004] build/in-game identity already promoted')

print('[AERIS25 ATROPINE REV004] TEMPORAL FOUNDATION OVERSCAN HOTFIX APPLIED')
print('Visible ND authority: unchanged exact user range / projection / 10 Hz')
print('Hidden foundation planning: existing 1.35x overscan, capped at 250 km')
print('No shader change; accepted AERIS25 AssetBundle may be reused')
