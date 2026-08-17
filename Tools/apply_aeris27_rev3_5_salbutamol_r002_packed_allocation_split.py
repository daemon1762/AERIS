#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R002 PACKED ALLOCATION SPLIT]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


if not R.is_file() or not B.is_file():
    fail('generated renderer/build missing')
renderer = R.read_text()
build = B.read_text()
if R001 not in renderer:
    fail('R001 generated parent required')
if 'YieldPendingEntryCommit(executedStage, stageStart, true)' not in renderer:
    fail('R001 Compile Hotfix1 three-argument yield contract required')
if R002 in renderer:
    print(PREFIX + ' patch already present')
    sys.exit(0)

renderer, _ = replace_once(
    renderer,
    '        const string Rev35Variant = "' + R001 + '";\n',
    '        const string Rev35Variant = "' + R001 + '";\n'
    '        const string Rev35R002Variant = "' + R002 + '";\n',
    'R002 runtime marker')

renderer, _ = replace_once(
    renderer,
    '        long operationHealthRev35PreparePackedYields;\n',
    '        long operationHealthRev35PreparePackedYields;\n'
    '        double operationHealthRev35PackedSourceAllocMaxMs;\n'
    '        double operationHealthRev35PackedColourAllocMaxMs;\n'
    '        double operationHealthRev35PackedIndexAllocMaxMs;\n',
    'R002 allocation telemetry fields')

method_start = renderer.find('        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,')
method_end = renderer.find('\n        Mesh UploadPreparedPackedTerrainMesh', method_start)
if method_start < 0 or method_end <= method_start:
    fail('AdvancePendingPackedTerrain method not found')
method = renderer[method_start:method_end]
case0 = method.find('                    case 0:')
case1 = method.find('                    case 1:', case0 + 1)
if case0 < 0 or case1 <= case0:
    fail('R001 packed case0/case1 anchors missing')

# Shift the original copy/index substages upward by three. The new cases 1/2/3 each
# contain exactly one unavoidable CLR array allocation and always yield afterwards, so
# three large allocations can never land in the same KSP frame again.
tail = method[case1:]
for n in range(9, 0, -1):
    tail = tail.replace('case %d:' % n, 'case %d:' % (n + 3))
    tail = tail.replace('pending.PrepareSubstage = %d;' % n,
                        'pending.PrepareSubstage = %d;' % (n + 3))

new_cases = r'''                    case 0:
                        pending.PackedWaterCount = waterSource == null ? 0 : waterSource.Length;
                        pending.PackedLandCount = landSource == null ? 0 : landSource.Length;
                        pending.PackedCoastalWaterCount = coastalWaterSource == null ? 0 :
                            coastalWaterSource.Length;
                        pending.PackedCoastalLandCount = coastalLandSource == null ? 0 :
                            coastalLandSource.Length;
                        pending.PackedWaterOffset = 0;
                        pending.PackedLandOffset = pending.PackedWaterCount;
                        pending.PackedCoastalWaterOffset = pending.PackedLandOffset +
                            pending.PackedLandCount;
                        pending.PackedCoastalLandOffset = pending.PackedCoastalWaterOffset +
                            pending.PackedCoastalWaterCount;
                        int vertexCount = pending.PackedCoastalLandOffset +
                            pending.PackedCoastalLandCount;
                        pending.PackedSourceMeshCount =
                            (pending.PackedWaterCount > 0 ? 1 : 0) +
                            (pending.PackedLandCount > 0 ? 1 : 0) +
                            (pending.PackedCoastalWaterCount > 0 ? 1 : 0) +
                            (pending.PackedCoastalLandCount > 0 ? 1 : 0);
                        int indexCount = pending.Water.Triangles.Count +
                            pending.Land.Triangles.Count +
                            pending.PackedCoastalWaterCount +
                            pending.PackedCoastalLandCount;
                        if (vertexCount < 3 || indexCount < 3 ||
                            pending.PackedSourceMeshCount <= 0)
                            return true;
                        pending.PrepareSubstage = 1;
                        pending.PrepareCursor = 0;
                        pending.PackedIndexWriteCursor = 0;
                        break;
                    case 1:
                        {
                            int count = pending.PackedCoastalLandOffset +
                                pending.PackedCoastalLandCount;
                            long started = Stopwatch.GetTimestamp();
                            pending.PackedSource = new Vector3[count];
                            double elapsed = (Stopwatch.GetTimestamp() - started) *
                                1000.0 / Stopwatch.Frequency;
                            if (elapsed > operationHealthRev35PackedSourceAllocMaxMs)
                                operationHealthRev35PackedSourceAllocMaxMs = elapsed;
                            pending.PrepareSubstage = 2;
                            operationHealthRev35PreparePackedYields++;
                            return false;
                        }
                    case 2:
                        {
                            int count = pending.PackedCoastalLandOffset +
                                pending.PackedCoastalLandCount;
                            long started = Stopwatch.GetTimestamp();
                            pending.PackedColours = new Color32[count];
                            double elapsed = (Stopwatch.GetTimestamp() - started) *
                                1000.0 / Stopwatch.Frequency;
                            if (elapsed > operationHealthRev35PackedColourAllocMaxMs)
                                operationHealthRev35PackedColourAllocMaxMs = elapsed;
                            pending.PrepareSubstage = 3;
                            operationHealthRev35PreparePackedYields++;
                            return false;
                        }
                    case 3:
                        {
                            int count = pending.Water.Triangles.Count +
                                pending.Land.Triangles.Count +
                                pending.PackedCoastalWaterCount +
                                pending.PackedCoastalLandCount;
                            long started = Stopwatch.GetTimestamp();
                            pending.PackedIndices = new int[count];
                            double elapsed = (Stopwatch.GetTimestamp() - started) *
                                1000.0 / Stopwatch.Frequency;
                            if (elapsed > operationHealthRev35PackedIndexAllocMaxMs)
                                operationHealthRev35PackedIndexAllocMaxMs = elapsed;
                            pending.PrepareSubstage = 4;
                            operationHealthRev35PreparePackedYields++;
                            return false;
                        }
'''
method = method[:case0] + new_cases + tail
renderer = renderer[:method_start] + method + renderer[method_end:]

renderer, _ = replace_once(
    renderer,
    '                "; oh_rev35_prepare_packed_yield=" + operationHealthRev35PreparePackedYields +\n',
    '                "; oh_rev35_prepare_packed_yield=" + operationHealthRev35PreparePackedYields +\n'
    '                "; oh_rev35_r002_variant=" + Rev35R002Variant +\n'
    '                "; oh_rev35_packed_source_alloc_max_ms=" + operationHealthRev35PackedSourceAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_packed_colour_alloc_max_ms=" + operationHealthRev35PackedColourAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_packed_index_alloc_max_ms=" + operationHealthRev35PackedIndexAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n',
    'R002 allocation telemetry append')

if 'REV3_5_R002_VARIANT="' + R002 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_VARIANT="' + R001 + '"\n',
        'REV3_5_VARIANT="' + R001 + '"\n'
        'REV3_5_R002_VARIANT="' + R002 + '"\n',
        'build R002 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py"\n',
        'build R002 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_variant=%s\\n\' "$REV3_5_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_variant=%s\\n\' "$REV3_5_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r002_variant=%s\\n\' "$REV3_5_R002_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R002 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R001)
print('r002=' + R002)
print('change=PACKED_ARRAY_ALLOCS_ONE_PER_FRAME + PER_ALLOC_MAX_TELEMETRY')
print('worker_prepare=0 speculative=0 presentation_cache=0')
