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
        raise SystemExit("[AERIS25 ATROPINE REV005] %s anchor mismatch old=%d" %
                         (label, count))
    return text.replace(old, new, 1), True


renderer = RENDERER.read_text()

field_old = '''        long operationHealthCullGuardVetoes;\n        long operationHealthCullGuardConfirmed;\n        float operationHealthContentVisibleRangeMeters;\n'''
field_new = '''        long operationHealthCullGuardVetoes;\n        long operationHealthCullGuardConfirmed;\n        long operationHealthFoundationCullBypass;\n        float operationHealthContentVisibleRangeMeters;\n'''
renderer, changed1 = replace_once(renderer, field_old, field_new,
                                  'foundation cull bypass telemetry field')

cull_old = '''                    if (entryCullingEnabled &&\n                        ShouldCullEntryOutsidePresentation(drawEntry,\n                            projection.CenterX, projection.CenterY, projection.CenterZ,\n                            viewportCullSin, viewportCullCos))\n                    {\n                        // AERIS25_CHUNK_CULL_GUARD: dot-cap remains the cheap broad phase,\n                        // but runtime evidence showed complete FAR Entries could still be\n                        // omitted as rectangular holes while foundation/coverage stayed\n                        // READY. Only candidates already rejected by dot-cap pay for this\n                        // 3x3 presentation witness. Any possible viewport intersection\n                        // vetoes the cull; uncertainty therefore costs work, never pixels.\n                        if (TileMayIntersectPresentation(tile, projection))\n                        {\n                            operationHealthCulledEntries = Math.Max(0L,\n                                operationHealthCulledEntries - 1L);\n                            operationHealthVisibleEntries++;\n                            operationHealthCullGuardVetoes++;\n                        }\n                        else\n                        {\n                            operationHealthCullGuardConfirmed++;\n                            continue;\n                        }\n                    }\n'''
cull_new = '''                    bool foundationEntry = tile.Key.Lod == AERISTerrainTileLod.Global ||\n                        tile.Key.Lod == AERISTerrainTileLod.Far;\n                    if (entryCullingEnabled && foundationEntry)\n                    {\n                        // AERIS25_FOUNDATION_CULL_BYPASS: Global/FAR entries in CaptureVisible\n                        // are the viewport foundation authority itself. Runtime rev003/rev004\n                        // proved that draw-time whole-Entry rejection can coexist with\n                        // foundation/coverage=1.000 and visible edge/ocean holes, including at\n                        // zero groundspeed. Foundation therefore fails open to GPU clipping.\n                        // Route/Local/Land detail keeps the accepted dot-cap + witness guard.\n                        operationHealthFoundationCullBypass++;\n                    }\n                    else if (entryCullingEnabled &&\n                        ShouldCullEntryOutsidePresentation(drawEntry,\n                            projection.CenterX, projection.CenterY, projection.CenterZ,\n                            viewportCullSin, viewportCullCos))\n                    {\n                        // AERIS25_CHUNK_CULL_GUARD remains authoritative for non-foundation\n                        // detail entries. Only dot-cap rejects pay for the 3x3 witness.\n                        if (TileMayIntersectPresentation(tile, projection))\n                        {\n                            operationHealthCulledEntries = Math.Max(0L,\n                                operationHealthCulledEntries - 1L);\n                            operationHealthVisibleEntries++;\n                            operationHealthCullGuardVetoes++;\n                        }\n                        else\n                        {\n                            operationHealthCullGuardConfirmed++;\n                            continue;\n                        }\n                    }\n'''
renderer, changed2 = replace_once(renderer, cull_old, cull_new,
                                  'Global/FAR foundation fail-open cull bypass')

telemetry_old = '''                "; oh_cull_guard_veto=" + operationHealthCullGuardVetoes +\n                "; oh_cull_guard_confirm=" + operationHealthCullGuardConfirmed +\n                "; oh_mesh_pool=" + meshPool.Count +\n'''
telemetry_new = '''                "; oh_cull_guard_veto=" + operationHealthCullGuardVetoes +\n                "; oh_cull_guard_confirm=" + operationHealthCullGuardConfirmed +\n                "; oh_foundation_cull_bypass=" + operationHealthFoundationCullBypass +\n                "; oh_mesh_pool=" + meshPool.Count +\n'''
renderer, changed3 = replace_once(renderer, telemetry_old, telemetry_new,
                                  'foundation cull bypass telemetry publication')

if any((changed1, changed2, changed3)):
    RENDERER.write_text(renderer)
    print('[AERIS25 ATROPINE REV005] Global/FAR foundation cull bypass applied')
else:
    print('[AERIS25 ATROPINE REV005] Global/FAR foundation cull bypass already present')

monitor = MONITOR.read_text()
if 'internal const string Revision = "OH_PHASE4_005";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_004";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV005] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_004";',
                              'internal const string Revision = "OH_PHASE4_005";', 1)
    MONITOR.write_text(monitor)
    print('[AERIS25 ATROPINE REV005] revision=OH_PHASE4_005')
else:
    print('[AERIS25 ATROPINE REV005] revision already OH_PHASE4_005')

build = BUILD.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV004 TEMPORAL FOUNDATION OVERSCAN"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV005 FOUNDATION CULL BYPASS"'
build, display_changed = replace_once(build, old_display, new_display,
                                      'in-game display revision')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV004 TEMPORAL FOUNDATION OVERSCAN";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV005 FOUNDATION CULL BYPASS";'
build, checkpoint_changed = replace_once(build, old_checkpoint, new_checkpoint,
                                         'in-game checkpoint revision')

overscan_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py"'
foundation_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_foundation_cull_bypass_hotfix.py"'
active_overscan = sum(1 for line in build.splitlines() if line.strip() == overscan_verify)
active_foundation = sum(1 for line in build.splitlines() if line.strip() == foundation_verify)
verify_changed = False
if active_overscan == 1 and active_foundation == 0:
    build = build.replace(overscan_verify, overscan_verify + "\n" + foundation_verify, 1)
    verify_changed = True
elif active_overscan == 1 and active_foundation == 1:
    pass
else:
    raise SystemExit('[AERIS25 ATROPINE REV005] build verifier gate mismatch overscan=%d foundation=%d' %
                     (active_overscan, active_foundation))

if display_changed or checkpoint_changed or verify_changed:
    BUILD.write_text(build)
    print('[AERIS25 ATROPINE REV005] build/in-game identity and verifier gate promoted')
else:
    print('[AERIS25 ATROPINE REV005] build/in-game identity already promoted')

print('[AERIS25 ATROPINE REV005] FOUNDATION CULL BYPASS HOTFIX APPLIED')
print('Global/FAR viewport foundation: never whole-Entry culled; GPU clip is final authority')
print('Route/Local/Land detail: existing dot-cap + rev003 projected witness retained')
print('rev004 1.35x hidden foundation overscan retained')
print('No shader change; accepted AERIS25 AssetBundle may be reused')
