#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R005 SPLIT WEIGHT FLOW LANES]'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
R005 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def method_slice(text, signature, next_signature):
    start = text.find(signature)
    end = text.find(next_signature, start + 1)
    if start < 0 or end <= start:
        fail('method anchor failed: ' + signature)
    return start, end, text[start:end]


if not R.is_file() or not B.is_file():
    fail('generated renderer/build missing')
renderer = R.read_text()
build = B.read_text()
if R004 not in renderer:
    fail('R004 generated parent required')
if R005 in renderer:
    print(PREFIX + ' patch already present')
    sys.exit(0)

renderer, _ = replace_once(
    renderer,
    '        const int Rev35R004PrepareChunkHigh = 256;\n',
    '        const int Rev35R004PrepareChunkHigh = 256;\n'
    '        // R005 keeps R004 adaptive throughput only for the lightweight packed lane.\n'
    '        // Geographic/source preparation is materially heavier per item and is hard\n'
    '        // capped at the R001-safe 64-item cadence to prevent 80 ms class bursts.\n'
    '        const string Rev35R005Variant = "' + R005 + '";\n'
    '        const int Rev35R005SourceChunkHardCap = 64;\n',
    'R005 identity/source hard cap')

renderer, _ = replace_once(
    renderer,
    '        int operationHealthRev35R004ChunkMaxItems;\n',
    '        int operationHealthRev35R004ChunkMaxItems;\n'
    '        long operationHealthRev35R005SourceHardCapWindows;\n'
    '        int operationHealthRev35R005PackedChunkMaxItems;\n',
    'R005 telemetry fields')

s0, s1, sources = method_slice(
    renderer,
    '        bool AdvancePendingSources(PendingEntryCommit pending,\n',
    '        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,\n')
sources, _ = replace_once(
    sources,
    '            int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);\n',
    '            int chunkItems = Rev35R005SourceChunkHardCap;\n'
    '            operationHealthRev35R005SourceHardCapWindows++;\n',
    'R005 source heavy-lane hard cap')
if '(iterations % chunkItems) == 0' not in sources:
    fail('R005 source budget checkpoint missing')
if 'ResolveRev35R004PrepareChunkItems(budgetMilliseconds)' in sources:
    fail('R005 source lane still adaptive')
renderer = renderer[:s0] + sources + renderer[s1:]

p0, p1, packed = method_slice(
    renderer,
    '        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,\n',
    '        Mesh UploadPreparedPackedTerrainMesh(')
packed, _ = replace_once(
    packed,
    '            int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);\n',
    '            int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);\n'
    '            operationHealthRev35R005PackedChunkMaxItems = Math.Max(\n'
    '                operationHealthRev35R005PackedChunkMaxItems, chunkItems);\n',
    'R005 packed lightweight-lane telemetry')
if '(iterations % chunkItems) == 0' not in packed:
    fail('R005 packed budget checkpoint missing')
renderer = renderer[:p0] + packed + renderer[p1:]

renderer, _ = replace_once(
    renderer,
    '                "; oh_rev35_r004_chunk_max_items=" + operationHealthRev35R004ChunkMaxItems +\n',
    '                "; oh_rev35_r004_chunk_max_items=" + operationHealthRev35R004ChunkMaxItems +\n'
    '                "; oh_rev35_r005_variant=" + Rev35R005Variant +\n'
    '                "; oh_rev35_r005_source_chunk_cap=" + Rev35R005SourceChunkHardCap +\n'
    '                "; oh_rev35_r005_source_windows=" + operationHealthRev35R005SourceHardCapWindows +\n'
    '                "; oh_rev35_r005_packed_chunk_max_items=" + operationHealthRev35R005PackedChunkMaxItems +\n',
    'R005 telemetry append')

if 'REV3_5_R005_VARIANT="' + R005 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R004_VARIANT="' + R004 + '"\n',
        'REV3_5_R004_VARIANT="' + R004 + '"\n'
        'REV3_5_R005_VARIANT="' + R005 + '"\n',
        'build R005 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py"\n',
        'build R005 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r004_variant=%s\\n\' "$REV3_5_R004_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r004_variant=%s\\n\' "$REV3_5_R004_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r005_variant=%s\\n\' "$REV3_5_R005_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R005 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R004)
print('r005=' + R005)
print('source_lane_chunk=64 HARD CAP')
print('packed_lane_chunk=64/128/256 adaptive')
print('commit_budget=R004 0.50/1.00/1.50/2.00 ms retained')
print('allocation_yield=R004 BUDGET_AWARE retained')
print('worker_count_change=0 speculative=0 presentation_cache=0 quality_change=0 authority_change=0')
