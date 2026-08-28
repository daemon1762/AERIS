#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
C = (ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
U = (ROOT / 'build_ubuntu.sh').read_text()
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


# This verifier remains the inherited ADENOSINE path contract. It may be run on the
# original Phase 5 authority or on the one explicitly admitted final descendant that
# superseded it without changing the persistent-presentation invariants checked below.
# Keep the descendant gate exact: later revisions must add their own explicit admission.
adenosine_phase5_identity = (
    'internal const string Codename = "ADENOSINE";' in M and
    'internal const string Revision = "OH_PHASE5_001";' in M and
    'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";' in M and
    'codename = ADENOSINE' in C
)

norepinephrine_rev003_descendant_identity = (
    'internal const string Codename = "NOREPINEPHRINE";' in M and
    'internal const string Revision = "OH_PHASE6_003";' in M and
    'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
    'codename = NOREPINEPHRINE' in C
)

ck(adenosine_phase5_identity or norepinephrine_rev003_descendant_identity,
   'ADENOSINE OH_PHASE5_001 authority or exact NOREPINEPHRINE OH_PHASE6_003 descendant is authoritative')

adenosine_phase5_build = (
    'CANDIDATE_NAME="AERIS25_PERSISTENT_PRESENTATION_BATCHING"' in U and
    'OPERATION HEALTH PHASE 5 ADENOSINE PERSISTENT PRESENTATION BATCHING REV001' in U and
    'OPERATION HEALTH PHASE 5 ADENOSINE — PERSISTENT PRESENTATION BATCHING — REV001' in U
)

norepinephrine_rev003_build = (
    'CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"' in U and
    'OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in U and
    'verify_aeris25_authoritative_publication_lifetime_hotfix.py' in U
)

ck(adenosine_phase5_build or
   (norepinephrine_rev003_descendant_identity and norepinephrine_rev003_build),
   'build/in-game identity is ADENOSINE Phase5 or exact admitted NOREPINEPHRINE rev003 descendant')

ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'struct PresentationPacket' in R and
   'internal AERISTerrainHeightTile Tile;' in R and
   'internal Entry Entry;' in R and
   'internal bool ExactDetailOverlay;' in R,
   'renderer defines compact immutable presentation packets')
ck('PresentationPacket[] presentationPackets = new PresentationPacket[0];' in R and
   'readonly HashSet<Entry> presentationEntryPins = new HashSet<Entry>();' in R and
   'int presentationPacketCount;' in R,
   'persistent packet storage and O(1) snapshot pin set are resident')
ck('void RefreshPresentationPackets(AERISTerrainHeightTile[] tiles, Entry[] drawEntries)' in R and
   'ReferenceEquals(presentationPackets[compactCount].Tile, tile)' in R and
   'ReferenceEquals(presentationPackets[compactCount].Entry, entry)' in R and
   'operationHealthPresentationPacketReuses++;' in R,
   'content ticks reuse an unchanged packet set instead of rebuilding it')
ck('RefreshPresentationPackets(tiles, drawEntriesScratch);' in R and
   R.count('RenderBackBuffer(presentationPackets, presentationPacketCount, projection,') == 2 and
   'RenderBackBuffer(tiles, drawEntriesScratch, projection,' not in R,
   'both normal and recovery BACK paths consume the persistent packet set')
ck('int compactCount = Math.Min(Math.Max(0, packetCount),' in R and
   'PresentationPacket packet = packets[i];' in R and
   'AERISTerrainHeightTile tile = packet.Tile;' in R and
   'Entry drawEntry = packet.Entry;' in R,
   'BACK submission loop iterates compact packets rather than sparse tile/entry arrays')
ck('if (entryRendered && packet.ExactDetailOverlay)' in R,
   'detail-overlay classification is packetized once per content authority change')
ck('bool protectedEntry = presentationEntryPins.Contains(entry);' in R and
   'for (int i = 0; i < drawEntriesScratch.Length; i++)' not in
       R[R.find('bool IsEntryProtectedByContentSnapshot'):R.find('bool PruneWarmResume', R.find('bool IsEntryProtectedByContentSnapshot'))],
   'rev008 snapshot Mesh lifetime protection now uses O(1) HashSet lookup')
ck('presentationPacketCount = 0;' in R and
   'presentationEntryPins.Clear();' in R and
   R.find('presentationPacketCount = 0;') < R.find('void RefreshPresentationPackets'),
   'content snapshot reset clears persistent packet/pin authority')
ck('oh_presentation_packet_count=' in R and
   'oh_presentation_packet_rebuild=' in R and
   'oh_presentation_packet_reuse=' in R and
   'oh_presentation_packet_slot_skip=' in R and
   'oh_presentation_pin_hit=' in R and
   'oh_presentation_pin_miss=' in R and
   'oh_presentation_packet_draw=' in R,
   'runtime publishes packet persistence/submission telemetry')

# AERIS25-1 accepted invariants must remain present on the final ADENOSINE path and
# on the explicitly admitted NOREPINEPHRINE rev003 descendant.
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and
   'oh_content_commit_budget_hit=' in R and
   'oh_prune_budget_hit=' in R and
   'oh_heading_plan_coalesced=' in R,
   'accepted ATROPINE rev009 burst governor remains intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R and
   'oh_snapshot_stale_mesh=' in R and
   'oh_gpu_vertex_reject_semantic_mesh_null=' in R,
   'rev008 lifetime guard and rev007 root-cause witness remain intact')
ck('AERIS25_CHUNK_CULL_GUARD' in R and
   'AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R and
   'operationHealthFoundationCullBypass++' not in R,
   'accepted cull guard/overscan path and rejected rev005 bypass state are preserved')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_PERSISTENT_PRESENTATION_BATCHING' not in SH,
   'inherited ADENOSINE path changes no shader equations or shader bytes')

# Painter order is a hard Golden contract. Packetization may not reorder across layers.
draw_start = R.find('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
draw_end = R.find('        static void EnsurePackedTerrainColours(Entry entry,', draw_start)
draw = R[draw_start:draw_end] if draw_start >= 0 and draw_end > draw_start else ''
terrain = draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)')
contour = draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)')
coast = draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)')
ck(0 <= terrain < contour < coast,
   'hard painter order remains terrain -> contour -> coastline inside every Entry')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz visible ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck('GPU_DYNAMIC_SEMANTIC' in R and 'oh_gpu_dynamic_colour=' in R,
   'accepted GPU Dynamic Terrain Colour authority remains present')

active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
phase5_active_contract = (
    'verify_aeris25_persistent_presentation_batching.py' in active and
    'verify_aeris25_content_generation_burst_governor_hotfix.py' not in active and
    'verify_aeris25_chunk_cull_guard_hotfix.py' not in active and
    'verify_aeris25_temporal_foundation_overscan_hotfix.py' not in active
)
rev003_active_contract = (
    'verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active and
    'verify_aeris25_persistent_presentation_batching.py' not in active and
    'verify_aeris25_content_generation_burst_governor_hotfix.py' not in active and
    'verify_aeris25_chunk_cull_guard_hotfix.py' not in active and
    'verify_aeris25_temporal_foundation_overscan_hotfix.py' not in active
)
ck((adenosine_phase5_identity and phase5_active_contract) or
   (norepinephrine_rev003_descendant_identity and rev003_active_contract),
   'active build uses the correct final-tree verifier for Phase5 or exact OH_PHASE6_003 descendant')

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
mode = ('ADENOSINE_PHASE5_001' if adenosine_phase5_identity else
        'NOREPINEPHRINE_OH_PHASE6_003_DESCENDANT')
print('\n[AERIS25 ADENOSINE INHERITED PRESENTATION PATH] mode=%s %d/%d PASS' %
      (mode, len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ADENOSINE INHERITED PRESENTATION PATH] STATIC PASS')
