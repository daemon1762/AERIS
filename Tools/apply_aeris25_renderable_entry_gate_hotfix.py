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
        raise SystemExit("[AERIS25 ATROPINE REV006] %s anchor mismatch old=%d" %
                         (label, count))
    return text.replace(old, new, 1), True


renderer = RENDERER.read_text()

# rev005 proved that disabling Global/FAR culling does not remove the holes and causes
# a severe submission/performance regression. Restore the accepted rev003 broad-phase +
# projected witness for all entries. Keep the rev005 telemetry field only as a runtime
# proof that the bypass is no longer executing (it must remain zero).
cull_old = '''                    bool foundationEntry = tile.Key.Lod == AERISTerrainTileLod.Global ||
                        tile.Key.Lod == AERISTerrainTileLod.Far;
                    if (entryCullingEnabled && foundationEntry)
                    {
                        // AERIS25_FOUNDATION_CULL_BYPASS: Global/FAR entries in CaptureVisible
                        // are the viewport foundation authority itself. Runtime rev003/rev004
                        // proved that draw-time whole-Entry rejection can coexist with
                        // foundation/coverage=1.000 and visible edge/ocean holes, including at
                        // zero groundspeed. Foundation therefore fails open to GPU clipping.
                        // Route/Local/Land detail keeps the accepted dot-cap + witness guard.
                        operationHealthFoundationCullBypass++;
                    }
                    else if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos))
                    {
                        // AERIS25_CHUNK_CULL_GUARD remains authoritative for non-foundation
                        // detail entries. Only dot-cap rejects pay for the 3x3 witness.
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
cull_new = '''                    // AERIS25_RENDERABLE_ENTRY_GATE: rev005 runtime proved
                    // foundation-cull bypass did not remove the holes and caused a severe
                    // submission regression. Restore rev003 dot-cap + fail-open witness for
                    // every Entry; hole correctness is now enforced at Entry promotion.
                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos))
                    {
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
renderer, changed1 = replace_once(renderer, cull_old, cull_new,
                                  'restore rev003 culling after rev005 rejection')

field_old = '''        long operationHealthFoundationCullBypass;
        float operationHealthContentVisibleRangeMeters;
'''
field_new = '''        long operationHealthFoundationCullBypass;
        long operationHealthNonRenderableEntryRejects;
        long operationHealthFallbackShadowPrevents;
        long operationHealthEmptyTriangleResults;
        float operationHealthContentVisibleRangeMeters;
'''
renderer, changed2 = replace_once(renderer, field_old, field_new,
                                  'renderable-entry telemetry fields')

# A non-renderable current Entry must never shadow an older drawable fallback.
draw_select_old = '''                    if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                    if (currentEntry != null) currentEntry.LastUse = ++useSequence;
                    fallbackEntriesScratch[i] = fallbackEntry;
                    currentEntriesScratch[i] = currentEntry;
                    drawEntriesScratch[i] = currentEntry != null ?
                        currentEntry : fallbackEntry;
'''
draw_select_new = '''                    bool currentRenderable = HasRenderableTerrain(currentEntry);
                    if (!currentRenderable && currentEntry != null && fallbackEntry != null)
                        operationHealthFallbackShadowPrevents++;
                    if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                    if (currentRenderable) currentEntry.LastUse = ++useSequence;
                    fallbackEntriesScratch[i] = fallbackEntry;
                    currentEntriesScratch[i] = currentRenderable ? currentEntry : null;
                    drawEntriesScratch[i] = currentRenderable ? currentEntry : fallbackEntry;
'''
renderer, changed3 = replace_once(renderer, draw_select_old, draw_select_new,
                                  'fail-safe current/fallback selection')

# Cached render-ready fields may yield an Entry object whose packed terrain mesh is null.
# Reject it before replacing a previously drawable Entry or publishing GPU-ready state.
try_upload_old = '''                entry = BuildEntry(cacheKey, field);
                Entry old;
                if (entries.TryGetValue(cacheKey, out old)) Remove(old);
'''
try_upload_new = '''                entry = BuildEntry(cacheKey, field);
                if (!HasRenderableTerrain(entry))
                {
                    operationHealthNonRenderableEntryRejects++;
                    // This immutable render-ready field cannot currently produce drawable
                    // terrain. Remove it so the normal scheduler may rebuild it, but retain
                    // any older renderable Entry/fallback already covering the viewport.
                    RemoveRenderReadyField(cacheKey, field);
                    entry = null;
                    return false;
                }
                Entry old;
                if (entries.TryGetValue(cacheKey, out old)) Remove(old);
'''
renderer, changed4 = replace_once(renderer, try_upload_old, try_upload_new,
                                  'cached render-ready promotion gate')

# Fresh worker results must obey the same invariant. In particular, zero-triangle results
# are not presentation-ready terrain and must never be stored/marked GPU ready.
valid_old = '''                AERISTerrainGpuTileRasterResult result = completed[i];
                if (!ValidResult(result)) continue;
                string cacheKey = CacheKey(result.Key, result.TileCreatedUtcTicks,
                    result.StyleKey);
'''
valid_new = '''                AERISTerrainGpuTileRasterResult result = completed[i];
                if (!ValidResult(result)) continue;
                if (result.Triangles.Length < 3)
                {
                    operationHealthEmptyTriangleResults++;
                    continue;
                }
                string cacheKey = CacheKey(result.Key, result.TileCreatedUtcTicks,
                    result.StyleKey);
'''
renderer, changed5 = replace_once(renderer, valid_old, valid_new,
                                  'empty raster result rejection')

fresh_old = '''                CaptureAndMarkRenderReady(result, system);
                long uploadStartTicks = Stopwatch.GetTimestamp();
                try
                {
                    Entry entry = BuildEntry(cacheKey, result);
                    Entry old;
'''
fresh_new = '''                long uploadStartTicks = Stopwatch.GetTimestamp();
                try
                {
                    Entry entry = BuildEntry(cacheKey, result);
                    if (!HasRenderableTerrain(entry))
                    {
                        operationHealthNonRenderableEntryRejects++;
                        RemoveRenderReadyField(cacheKey, result);
                        continue;
                    }
                    // Render-ready/RAM-resident authority is published only after the
                    // replacement proves that it can actually draw terrain.
                    CaptureAndMarkRenderReady(result, system);
                    Entry old;
'''
renderer, changed6 = replace_once(renderer, fresh_old, fresh_new,
                                  'fresh worker result promotion gate')

readiness_old = '''                if (current == null || current.CoverageFraction < 0.999f) continue;
'''
readiness_new = '''                if (!HasRenderableTerrain(current) ||
                    current.CoverageFraction < 0.999f) continue;
'''
renderer, changed7 = replace_once(renderer, readiness_old, readiness_new,
                                  'foundation readiness renderability invariant')

telemetry_old = '''                "; oh_foundation_cull_bypass=" + operationHealthFoundationCullBypass +
                "; oh_mesh_pool=" + meshPool.Count +
'''
telemetry_new = '''                "; oh_foundation_cull_bypass=" + operationHealthFoundationCullBypass +
                "; oh_nonrenderable_entry_reject=" + operationHealthNonRenderableEntryRejects +
                "; oh_fallback_shadow_prevent=" + operationHealthFallbackShadowPrevents +
                "; oh_empty_triangle_result=" + operationHealthEmptyTriangleResults +
                "; oh_mesh_pool=" + meshPool.Count +
'''
renderer, changed8 = replace_once(renderer, telemetry_old, telemetry_new,
                                  'renderable-entry telemetry publication')

if any((changed1, changed2, changed3, changed4, changed5, changed6, changed7, changed8)):
    RENDERER.write_text(renderer)
    print('[AERIS25 ATROPINE REV006] renderable Entry promotion gate applied')
else:
    print('[AERIS25 ATROPINE REV006] renderable Entry promotion gate already present')

monitor = MONITOR.read_text()
if 'internal const string Revision = "OH_PHASE4_006";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_005";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV006] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_005";',
                              'internal const string Revision = "OH_PHASE4_006";', 1)
    MONITOR.write_text(monitor)
    print('[AERIS25 ATROPINE REV006] revision=OH_PHASE4_006')
else:
    print('[AERIS25 ATROPINE REV006] revision already OH_PHASE4_006')

build = BUILD.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV005 FOUNDATION CULL BYPASS"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV006 RENDERABLE ENTRY GATE"'
build, display_changed = replace_once(build, old_display, new_display,
                                      'in-game display revision')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV005 FOUNDATION CULL BYPASS";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV006 RENDERABLE ENTRY GATE";'
build, checkpoint_changed = replace_once(build, old_checkpoint, new_checkpoint,
                                         'in-game checkpoint revision')

foundation_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_foundation_cull_bypass_hotfix.py"'
renderable_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_renderable_entry_gate_hotfix.py"'
active_foundation = sum(1 for line in build.splitlines() if line.strip() == foundation_verify)
active_renderable = sum(1 for line in build.splitlines() if line.strip() == renderable_verify)
verify_changed = False
if active_foundation == 1 and active_renderable == 0:
    build = build.replace(foundation_verify, renderable_verify, 1)
    verify_changed = True
elif active_foundation == 0 and active_renderable == 1:
    pass
else:
    raise SystemExit('[AERIS25 ATROPINE REV006] build verifier gate mismatch foundation=%d renderable=%d' %
                     (active_foundation, active_renderable))

if display_changed or checkpoint_changed or verify_changed:
    BUILD.write_text(build)
    print('[AERIS25 ATROPINE REV006] build/in-game identity and verifier gate promoted')
else:
    print('[AERIS25 ATROPINE REV006] build/in-game identity already promoted')

print('[AERIS25 ATROPINE REV006] RENDERABLE ENTRY GATE HOTFIX APPLIED')
print('Rejected diagnosis: foundation whole-Entry culling as hole root cause')
print('Current authority: rev003 culling restored; non-renderable current never replaces/shadows drawable fallback')
print('No shader change; accepted AERIS25 AssetBundle may be reused')
