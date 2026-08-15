#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
M = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
U = ROOT / "build_ubuntu.sh"


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS25 ATROPINE REV008] %s anchor mismatch old=%d" % (label, count))
    return text.replace(old, new, 1), True


renderer = R.read_text()
field_old = '''        long operationHealthWarmPruneTicks;
        long operationHealthWarmPruneRemoved;
        long operationHealthWarmPruneDeferrals;
'''
field_new = '''        long operationHealthWarmPruneTicks;
        long operationHealthWarmPruneRemoved;
        long operationHealthWarmPruneDeferrals;
        // AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD: a content snapshot reuses the
        // selected Entry references across motion-only 10 Hz presentations. Pruning
        // must not recycle Mesh objects still referenced by that immutable snapshot.
        long operationHealthSnapshotMeshPruneProtected;
        long operationHealthSnapshotMeshPruneDeferrals;
        long operationHealthSnapshotStaleMeshDetections;
'''
renderer, c1 = replace_once(renderer, field_old, field_new,
                            'snapshot mesh lifetime telemetry fields')

helper_anchor = '''        bool PruneWarmResume(long totalLimit, int maximumRemovals)
'''
helper = '''        bool IsEntryProtectedByContentSnapshot(Entry entry)
        {
            if (entry == null || drawEntriesScratch == null) return false;
            for (int i = 0; i < drawEntriesScratch.Length; i++)
                if (ReferenceEquals(drawEntriesScratch[i], entry)) return true;
            return false;
        }

'''
if 'AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in renderer and \
   'bool IsEntryProtectedByContentSnapshot(Entry entry)' not in renderer:
    if renderer.count(helper_anchor) != 1:
        raise SystemExit('[AERIS25 ATROPINE REV008] prune helper anchor mismatch')
    renderer = renderer.replace(helper_anchor, helper + helper_anchor, 1)
    c2 = True
else:
    c2 = False

warm_old = '''                foreach (Entry entry in entries.Values)
                {
                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;
                }
                if (oldest == null) break;
                Remove(oldest);
'''
warm_new = '''                foreach (Entry entry in entries.Values)
                {
                    if (IsEntryProtectedByContentSnapshot(entry))
                    {
                        operationHealthSnapshotMeshPruneProtected++;
                        continue;
                    }
                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;
                }
                if (oldest == null)
                {
                    operationHealthSnapshotMeshPruneDeferrals++;
                    break;
                }
                Remove(oldest);
'''
# This exact block exists twice: WarmPrune and normal Prune. Replace both deliberately.
if warm_new not in renderer:
    count = renderer.count(warm_old)
    if count != 2:
        raise SystemExit('[AERIS25 ATROPINE REV008] expected two prune selection blocks, found %d' % count)
    renderer = renderer.replace(warm_old, warm_new)
    c3 = True
else:
    c3 = False

stale_old = '''                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?
                        drawEntries[i] : null;
                    if (drawEntry == null) continue;
'''
stale_new = '''                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?
                        drawEntries[i] : null;
                    if (drawEntry == null) continue;
                    // Diagnostic witness for the exact rev007 failure class. A non-zero
                    // value after rev008 means some non-prune path still invalidated a
                    // snapshot-owned Mesh and must fail visual acceptance.
                    if (!HasRenderableTerrain(drawEntry))
                        operationHealthSnapshotStaleMeshDetections++;
'''
renderer, c4 = replace_once(renderer, stale_old, stale_new,
                            'stale snapshot mesh witness')

telemetry_old = '''                "; oh_nd_warm_prune_ticks=" + operationHealthWarmPruneTicks +
                "; oh_nd_warm_prune_removed=" + operationHealthWarmPruneRemoved +
                "; oh_nd_warm_prune_deferred=" + operationHealthWarmPruneDeferrals +
'''
telemetry_new = '''                "; oh_nd_warm_prune_ticks=" + operationHealthWarmPruneTicks +
                "; oh_nd_warm_prune_removed=" + operationHealthWarmPruneRemoved +
                "; oh_nd_warm_prune_deferred=" + operationHealthWarmPruneDeferrals +
                "; oh_snapshot_mesh_prune_protect=" + operationHealthSnapshotMeshPruneProtected +
                "; oh_snapshot_mesh_prune_defer=" + operationHealthSnapshotMeshPruneDeferrals +
                "; oh_snapshot_stale_mesh=" + operationHealthSnapshotStaleMeshDetections +
'''
renderer, c5 = replace_once(renderer, telemetry_old, telemetry_new,
                            'snapshot mesh lifetime telemetry publication')

if any((c1, c2, c3, c4, c5)):
    R.write_text(renderer)
    print('[AERIS25 ATROPINE REV008] snapshot-owned Mesh lifetime guard applied')
else:
    print('[AERIS25 ATROPINE REV008] snapshot-owned Mesh lifetime guard already present')

monitor = M.read_text()
if 'internal const string Revision = "OH_PHASE4_008";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_007";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV008] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_007";',
                              'internal const string Revision = "OH_PHASE4_008";', 1)
    M.write_text(monitor)
    print('[AERIS25 ATROPINE REV008] revision=OH_PHASE4_008')
else:
    print('[AERIS25 ATROPINE REV008] revision already OH_PHASE4_008')

build = U.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV007 GPU VERTEX REJECT DIAGNOSTICS"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV008 SNAPSHOT MESH LIFETIME GUARD"'
build, b1 = replace_once(build, old_display, new_display, 'build display identity')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV007 GPU VERTEX REJECT DIAGNOSTICS";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV008 SNAPSHOT MESH LIFETIME GUARD";'
build, b2 = replace_once(build, old_checkpoint, new_checkpoint, 'checkpoint identity')
old_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py"'
new_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py"'
if new_verify not in build:
    if build.count(old_verify) != 1:
        raise SystemExit('[AERIS25 ATROPINE REV008] active rev007 verifier anchor mismatch')
    build = build.replace(old_verify, new_verify, 1)
    b3 = True
else:
    b3 = False
if any((b1, b2, b3)):
    U.write_text(build)
    print('[AERIS25 ATROPINE REV008] build identity/verifier promoted')
else:
    print('[AERIS25 ATROPINE REV008] build identity/verifier already promoted')

# Final build still runs READY + rev003 + rev004 before the active rev008 verifier.
# Promote only those inherited final-tree allowlists here, in one place, so a new
# successor cannot recreate the rev007 verifier-whack-a-mole failure.
def promote_final_tree_verifier(path, variable='M'):
    text = path.read_text()
    if 'OH_PHASE4_008' not in text:
        needle = "   ('internal const string Revision = \"OH_PHASE4_007\";' in %s)" % variable
        if needle not in text:
            raise SystemExit('[AERIS25 ATROPINE REV008] descendant revision anchor missing in ' + path.name)
        text = text.replace(needle,
            needle + " or\n   ('internal const string Revision = \"OH_PHASE4_008\";' in %s)" % variable,
            1)
    if path.name == 'verify_aeris25_chunk_cull_guard_hotfix.py' and \
       "('REV008 SNAPSHOT MESH LIFETIME GUARD' in U)" not in text:
        needle = "   ('REV007 GPU VERTEX REJECT DIAGNOSTICS' in U),"
        if needle not in text:
            raise SystemExit('[AERIS25 ATROPINE REV008] rev003 build descendant anchor missing')
        text = text.replace(needle,
            "   ('REV007 GPU VERTEX REJECT DIAGNOSTICS' in U) or\n   ('REV008 SNAPSHOT MESH LIFETIME GUARD' in U),", 1)
    if path.name == 'verify_aeris25_temporal_foundation_overscan_hotfix.py' and \
       "('REV008 SNAPSHOT MESH LIFETIME GUARD' in U)" not in text:
        needle = "    ('REV007 GPU VERTEX REJECT DIAGNOSTICS' in U)) and"
        if needle not in text:
            raise SystemExit('[AERIS25 ATROPINE REV008] rev004 build descendant anchor missing')
        text = text.replace(needle,
            "    ('REV007 GPU VERTEX REJECT DIAGNOSTICS' in U) or\n    ('REV008 SNAPSHOT MESH LIFETIME GUARD' in U)) and", 1)
    path.write_text(text)

core = ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour.py'
core_text = core.read_text()
if '"OH_PHASE4_008"' not in core_text:
    old = '"OH_PHASE4_006", "OH_PHASE4_007")'
    new = '"OH_PHASE4_006", "OH_PHASE4_007", "OH_PHASE4_008")'
    if old not in core_text:
        raise SystemExit('[AERIS25 ATROPINE REV008] core accepted-revision anchor missing')
    core.write_text(core_text.replace(old, new, 1))

promote_final_tree_verifier(ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py', 'MON')
promote_final_tree_verifier(ROOT / 'Tools/verify_aeris25_chunk_cull_guard_hotfix.py', 'M')
promote_final_tree_verifier(ROOT / 'Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py', 'M')

print('[AERIS25 ATROPINE REV008] SNAPSHOT MESH LIFETIME GUARD HOTFIX APPLIED')
print('Invariant: Prune may not recycle a Mesh referenced by current content drawEntriesScratch')
print('Expected runtime: oh_snapshot_stale_mesh=0 and oh_gpu_vertex_reject_semantic_mesh_null stops increasing')
print('No shader/cull/10Hz/Golden/Runway/control-law change')
