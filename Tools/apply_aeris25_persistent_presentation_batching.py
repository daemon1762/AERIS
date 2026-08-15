#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
M = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
C = ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg'
U = ROOT / 'build_ubuntu.sh'


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit('[AERIS25 ADENOSINE PHASE5_001] %s anchor mismatch old=%d' %
                         (label, count))
    return text.replace(old, new, 1), True


renderer = R.read_text()

packet_old = '''        }\n\n        struct SurfacePoint\n        {\n'''
packet_new = '''        }\n\n        // AERIS25_PERSISTENT_PRESENTATION_BATCHING: immutable submission packet for the\n        // current content snapshot. Packets compact away empty tile slots and keep the\n        // exact per-Entry painter contract (terrain -> contour -> coastline). They are\n        // rebuilt only when content authority changes and reused on motion-only 10 Hz ticks.\n        struct PresentationPacket\n        {\n            internal AERISTerrainHeightTile Tile;\n            internal Entry Entry;\n            internal bool ExactDetailOverlay;\n        }\n\n        struct SurfacePoint\n        {\n'''
renderer, c1 = replace_once(renderer, packet_old, packet_new,
                            'presentation packet type')

fields_old = '''        Entry[] fallbackEntriesScratch = new Entry[0];\n        Entry[] currentEntriesScratch = new Entry[0];\n        Entry[] drawEntriesScratch = new Entry[0];\n'''
fields_new = '''        Entry[] fallbackEntriesScratch = new Entry[0];\n        Entry[] currentEntriesScratch = new Entry[0];\n        Entry[] drawEntriesScratch = new Entry[0];\n        // Persistent compact presentation set. The HashSet is also the rev008 Mesh\n        // lifetime pin authority, replacing an O(N) scan for every prune candidate.\n        PresentationPacket[] presentationPackets = new PresentationPacket[0];\n        int presentationPacketCount;\n        readonly HashSet<Entry> presentationEntryPins = new HashSet<Entry>();\n        long operationHealthPresentationPacketRebuilds;\n        long operationHealthPresentationPacketReuses;\n        long operationHealthPresentationPacketSlotsSkipped;\n        long operationHealthPresentationPinHits;\n        long operationHealthPresentationPinMisses;\n        long operationHealthPresentationPacketDraws;\n'''
renderer, c2 = replace_once(renderer, fields_old, fields_new,
                            'persistent presentation fields')

refresh_old = '''                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,\n                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);\n'''
refresh_new = '''                RefreshPresentationPackets(tiles, drawEntriesScratch);\n                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,\n                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);\n'''
renderer, c3 = replace_once(renderer, refresh_old, refresh_new,
                            'presentation packet refresh')

reuse_old = '''            else\n            {\n                operationHealthMotionOnlyTicks++;\n                if (!contentSnapshotValid || visible == null || tiles == null ||\n'''
reuse_new = '''            else\n            {\n                operationHealthMotionOnlyTicks++;\n                if (contentSnapshotValid) operationHealthPresentationPacketReuses++;\n                if (!contentSnapshotValid || visible == null || tiles == null ||\n'''
renderer, c4 = replace_once(renderer, reuse_old, reuse_new,
                            'motion-only packet reuse telemetry')

old_call = 'RenderBackBuffer(tiles, drawEntriesScratch, projection,'
new_call = 'RenderBackBuffer(presentationPackets, presentationPacketCount, projection,'
if new_call not in renderer:
    count = renderer.count(old_call)
    if count != 2:
        raise SystemExit('[AERIS25 ADENOSINE PHASE5_001] RenderBackBuffer call count=%d' % count)
    renderer = renderer.replace(old_call, new_call)
    c5 = True
else:
    c5 = False

helper_old = '''            contentSnapshotValid = false;\n            contentGpuReadyPending = false;\n            nextContentMaintenanceRealtime = 0f;\n        }\n\n        bool RenderBackBuffer(AERISTerrainHeightTile[] tiles, Entry[] drawEntries,\n'''
helper_new = '''            contentSnapshotValid = false;\n            contentGpuReadyPending = false;\n            nextContentMaintenanceRealtime = 0f;\n            presentationPacketCount = 0;\n            presentationEntryPins.Clear();\n        }\n\n        void RefreshPresentationPackets(AERISTerrainHeightTile[] tiles, Entry[] drawEntries)\n        {\n            int sourceCount = Math.Min(tiles == null ? 0 : tiles.Length,\n                drawEntries == null ? 0 : drawEntries.Length);\n            int compactCount = 0;\n            bool unchanged = presentationPacketCount > 0;\n            for (int i = 0; i < sourceCount; i++)\n            {\n                AERISTerrainHeightTile tile = tiles[i];\n                Entry entry = drawEntries[i];\n                if (tile == null || entry == null) continue;\n                bool exactDetail = tile.Key.Lod >= AERISTerrainTileLod.Route;\n                if (unchanged && (compactCount >= presentationPacketCount ||\n                    !ReferenceEquals(presentationPackets[compactCount].Tile, tile) ||\n                    !ReferenceEquals(presentationPackets[compactCount].Entry, entry) ||\n                    presentationPackets[compactCount].ExactDetailOverlay != exactDetail))\n                    unchanged = false;\n                compactCount++;\n            }\n            if (unchanged && compactCount == presentationPacketCount)\n            {\n                operationHealthPresentationPacketReuses++;\n                return;\n            }\n\n            if (presentationPackets == null || presentationPackets.Length < sourceCount)\n                presentationPackets = new PresentationPacket[sourceCount];\n            presentationEntryPins.Clear();\n            int count = 0;\n            for (int i = 0; i < sourceCount; i++)\n            {\n                AERISTerrainHeightTile tile = tiles[i];\n                Entry entry = drawEntries[i];\n                if (tile == null || entry == null)\n                {\n                    operationHealthPresentationPacketSlotsSkipped++;\n                    continue;\n                }\n                presentationPackets[count++] = new PresentationPacket\n                {\n                    Tile = tile,\n                    Entry = entry,\n                    ExactDetailOverlay = tile.Key.Lod >= AERISTerrainTileLod.Route\n                };\n                presentationEntryPins.Add(entry);\n            }\n            for (int i = count; i < presentationPacketCount; i++)\n                presentationPackets[i] = default(PresentationPacket);\n            presentationPacketCount = count;\n            operationHealthPresentationPacketRebuilds++;\n        }\n\n        bool RenderBackBuffer(PresentationPacket[] packets, int packetCount,\n'''
renderer, c6 = replace_once(renderer, helper_old, helper_new,
                            'persistent packet helper and render signature')

loop_old = '''                for (int i = 0; i < tiles.Length; i++)\n                {\n                    AERISTerrainHeightTile tile = tiles[i];\n                    if (tile == null) continue;\n                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?\n                        drawEntries[i] : null;\n                    if (drawEntry == null) continue;\n'''
loop_new = '''                int compactCount = Math.Min(Math.Max(0, packetCount),\n                    packets == null ? 0 : packets.Length);\n                for (int i = 0; i < compactCount; i++)\n                {\n                    PresentationPacket packet = packets[i];\n                    AERISTerrainHeightTile tile = packet.Tile;\n                    Entry drawEntry = packet.Entry;\n                    if (tile == null || drawEntry == null) continue;\n                    operationHealthPresentationPacketDraws++;\n'''
renderer, c7 = replace_once(renderer, loop_old, loop_new,
                            'compact packet render loop')

detail_old = '''                    if (entryRendered && tile.Key.Lod >= AERISTerrainTileLod.Route)\n                        exactDetailOverlayDraws++;\n'''
detail_new = '''                    if (entryRendered && packet.ExactDetailOverlay)\n                        exactDetailOverlayDraws++;\n'''
renderer, c8 = replace_once(renderer, detail_old, detail_new,
                            'packet detail flag')

pin_old = '''        bool IsEntryProtectedByContentSnapshot(Entry entry)\n        {\n            if (entry == null || drawEntriesScratch == null) return false;\n            for (int i = 0; i < drawEntriesScratch.Length; i++)\n                if (ReferenceEquals(drawEntriesScratch[i], entry)) return true;\n            return false;\n        }\n'''
pin_new = '''        bool IsEntryProtectedByContentSnapshot(Entry entry)\n        {\n            if (entry == null) return false;\n            bool protectedEntry = presentationEntryPins.Contains(entry);\n            if (protectedEntry) operationHealthPresentationPinHits++;\n            else operationHealthPresentationPinMisses++;\n            return protectedEntry;\n        }\n'''
renderer, c9 = replace_once(renderer, pin_old, pin_new,
                            'O(1) snapshot pin lookup')

telemetry_old = '''                "; oh_heading_plan_coalesced=" + operationHealthContentHeadingCoalesced +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +\n'''
telemetry_new = '''                "; oh_heading_plan_coalesced=" + operationHealthContentHeadingCoalesced +\n                "; oh_presentation_packet_count=" + presentationPacketCount +\n                "; oh_presentation_packet_rebuild=" + operationHealthPresentationPacketRebuilds +\n                "; oh_presentation_packet_reuse=" + operationHealthPresentationPacketReuses +\n                "; oh_presentation_packet_slot_skip=" + operationHealthPresentationPacketSlotsSkipped +\n                "; oh_presentation_pin_hit=" + operationHealthPresentationPinHits +\n                "; oh_presentation_pin_miss=" + operationHealthPresentationPinMisses +\n                "; oh_presentation_packet_draw=" + operationHealthPresentationPacketDraws +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +\n'''
renderer, c10 = replace_once(renderer, telemetry_old, telemetry_new,
                             'presentation telemetry')

if any((c1, c2, c3, c4, c5, c6, c7, c8, c9, c10)):
    R.write_text(renderer)
    print('[AERIS25 ADENOSINE PHASE5_001] persistent presentation batching applied')
else:
    print('[AERIS25 ADENOSINE PHASE5_001] persistent presentation batching already present')

monitor = M.read_text()
if 'internal const string Codename = "ADENOSINE";' not in monitor:
    if monitor.count('internal const string Codename = "ATROPINE";') != 1:
        raise SystemExit('[AERIS25 ADENOSINE PHASE5_001] codename anchor mismatch')
    monitor = monitor.replace('internal const string Codename = "ATROPINE";',
                              'internal const string Codename = "ADENOSINE";', 1)
if 'internal const string Revision = "OH_PHASE5_001";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_009";') != 1:
        raise SystemExit('[AERIS25 ADENOSINE PHASE5_001] revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_009";',
                              'internal const string Revision = "OH_PHASE5_001";', 1)
if 'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";' not in monitor:
    if monitor.count('internal const string Candidate = "AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR";') != 1:
        raise SystemExit('[AERIS25 ADENOSINE PHASE5_001] candidate anchor mismatch')
    monitor = monitor.replace(
        'internal const string Candidate = "AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR";',
        'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";', 1)
M.write_text(monitor)

config = C.read_text()
if 'codename = ADENOSINE' not in config:
    if config.count('codename = ATROPINE') != 1:
        raise SystemExit('[AERIS25 ADENOSINE PHASE5_001] config codename anchor mismatch')
    config = config.replace('codename = ATROPINE', 'codename = ADENOSINE', 1)
    C.write_text(config)

build = U.read_text()
old_candidate = 'CANDIDATE_NAME="AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR"'
new_candidate = 'CANDIDATE_NAME="AERIS25_PERSISTENT_PRESENTATION_BATCHING"'
build, b1 = replace_once(build, old_candidate, new_candidate,
                         'build candidate identity')
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV009 CONTENT GENERATION BURST GOVERNOR"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 5 ADENOSINE PERSISTENT PRESENTATION BATCHING REV001"'
build, b2 = replace_once(build, old_display, new_display,
                         'build display identity')
old_checkpoint = 'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV009 CONTENT GENERATION BURST GOVERNOR'
new_checkpoint = 'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 5 ADENOSINE — PERSISTENT PRESENTATION BATCHING — REV001'
build, b3 = replace_once(build, old_checkpoint, new_checkpoint,
                         'checkpoint identity')
old_matrix = '''PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py"\nPYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_chunk_cull_guard_hotfix.py"\nPYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py"\nPYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_content_generation_burst_governor_hotfix.py"\n'''
new_matrix = '''# AERIS25-1 inherited acceptance is verified before ADENOSINE promotion.\n# Phase 5 reasserts all frozen visual/control invariants on the final generated tree.\nPYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_persistent_presentation_batching.py"\n'''
build, b4 = replace_once(build, old_matrix, new_matrix,
                         'Phase 5 verifier matrix')
if any((b1, b2, b3, b4)):
    U.write_text(build)
    print('[AERIS25 ADENOSINE PHASE5_001] build identity/verifier promoted')
else:
    print('[AERIS25 ADENOSINE PHASE5_001] build identity/verifier already promoted')

print('[AERIS25 ADENOSINE PHASE5_001] PERSISTENT PRESENTATION / SUBMISSION BATCHING APPLIED')
print('Invariant: visible 10 Hz projection and exact per-Entry terrain->contour->coast order are unchanged')
print('Expected runtime: packet_reuse >> packet_rebuild during steady motion; snapshot_stale_mesh=0')
