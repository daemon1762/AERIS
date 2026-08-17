#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001]'
MARKER = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def replace_method(text, signature, next_signature, replacement, label):
    pattern = re.compile(r'\n        ' + re.escape(signature) + r'.*?(?=\n        ' +
                         re.escape(next_signature) + r')', re.S)
    matches = list(pattern.finditer(text))
    if len(matches) != 1:
        fail('%s method anchor count=%d' % (label, len(matches)))
    start, end = matches[0].span()
    return text[:start] + '\n' + replacement.rstrip() + '\n' + text[end:]


if not R.is_file() or not B.is_file():
    fail('required generated runtime files are missing')

renderer = R.read_text()
build = B.read_text()
if MARKER in renderer:
    print(PREFIX + ' patch already present')
    sys.exit(0)

for required in (
    'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION',
    'AERIS25_STAGED_MAIN_THREAD_COMMIT',
    'PendingEntryCommitStage.PrepareSources',
    'PendingEntryCommitStage.PreparePackedTerrain',
    'void PreparePendingSources(PendingEntryCommit pending)',
    'void PreparePendingPackedTerrain(PendingEntryCommit pending)',
):
    if required not in renderer:
        fail('REV003 generated parent missing: ' + required)

for forbidden in (
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
    'WaitManagedPreparation',
    'ResidentPreparedPresentation',
):
    if forbidden in renderer:
        fail('refusing rejected descendant runtime: ' + forbidden)

renderer, _ = replace_once(
    renderer,
    '        enum PendingEntryCommitStage\n',
    '        // ' + MARKER + ': keep preparation on the Unity main thread, but make the\n'
    '        // managed source/packing work resumable. No partial Entry is published.\n'
    '        const string Rev35Variant = "' + MARKER + '";\n'
    '        const int Rev35PrepareChunkItems = 64;\n\n'
    '        enum PendingEntryCommitStage\n',
    'REV3.5 marker/constants')

renderer, _ = replace_once(
    renderer,
    '            internal int GeographicCursor;\n'
    '            internal float[] LandElevation;\n',
    '            internal int GeographicCursor;\n'
    '            internal int PrepareSubstage;\n'
    '            internal int PrepareCursor;\n'
    '            internal int PackedIndexWriteCursor;\n'
    '            internal float[] LandElevation;\n',
    'pending resumable cursors')

renderer, _ = replace_once(
    renderer,
    '        long operationHealthMainCommitPublishes;\n',
    '        long operationHealthMainCommitPublishes;\n'
    '        long operationHealthRev35PrepareSourceYields;\n'
    '        long operationHealthRev35PreparePackedYields;\n',
    'REV3.5 prepare telemetry fields')

renderer, _ = replace_once(
    renderer,
    '                    case PendingEntryCommitStage.PrepareSources:\n'
    '                        PreparePendingSources(pending);\n'
    '                        pending.Stage = PendingEntryCommitStage.PreparePackedTerrain;\n'
    '                        break;\n',
    '                    case PendingEntryCommitStage.PrepareSources:\n'
    '                        if (!AdvancePendingSources(pending, budgetMilliseconds))\n'
    '                            return YieldPendingEntryCommit(stageStart, true);\n'
    '                        pending.PrepareSubstage = 0;\n'
    '                        pending.PrepareCursor = 0;\n'
    '                        pending.Stage = PendingEntryCommitStage.PreparePackedTerrain;\n'
    '                        break;\n',
    'PrepareSources resumable switch')

renderer, _ = replace_once(
    renderer,
    '                    case PendingEntryCommitStage.PreparePackedTerrain:\n'
    '                        PreparePendingPackedTerrain(pending);\n'
    '                        pending.Stage = PendingEntryCommitStage.UploadPackedTerrain;\n'
    '                        break;\n',
    '                    case PendingEntryCommitStage.PreparePackedTerrain:\n'
    '                        if (!AdvancePendingPackedTerrain(pending, budgetMilliseconds))\n'
    '                            return YieldPendingEntryCommit(stageStart, true);\n'
    '                        pending.PrepareSubstage = 0;\n'
    '                        pending.PrepareCursor = 0;\n'
    '                        pending.Stage = PendingEntryCommitStage.UploadPackedTerrain;\n'
    '                        break;\n',
    'PreparePackedTerrain resumable switch')

sources_method = r'''        bool AdvancePendingSources(PendingEntryCommit pending,
            double budgetMilliseconds)
        {
            while (true)
            {
                int iterations = 0;
                switch (pending.PrepareSubstage)
                {
                    case 0:
                        pending.LandSource = pending.Land.Vertices.Count <= 0 ? null :
                            new Vector3[pending.Land.Vertices.Count];
                        pending.WaterSource = pending.Water.Vertices.Count <= 0 ? null :
                            new Vector3[pending.Water.Vertices.Count];
                        pending.LandElevation = new float[pending.Land.Elevation.Count];
                        pending.LandShade = new byte[pending.Land.Shade.Count];
                        pending.PrepareSubstage = 1;
                        pending.PrepareCursor = 0;
                        if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                            budgetMilliseconds)
                        {
                            operationHealthRev35PrepareSourceYields++;
                            return false;
                        }
                        break;
                    case 1:
                        if (pending.LandSource != null)
                        {
                            while (pending.PrepareCursor < pending.LandSource.Length)
                            {
                                pending.LandSource[pending.PrepareCursor] =
                                    pending.Land.Vertices[pending.PrepareCursor];
                                pending.PrepareCursor++;
                                iterations++;
                                if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                    budgetMilliseconds)
                                {
                                    operationHealthRev35PrepareSourceYields++;
                                    return false;
                                }
                            }
                        }
                        pending.PrepareSubstage = 2;
                        pending.PrepareCursor = 0;
                        break;
                    case 2:
                        if (pending.WaterSource != null)
                        {
                            while (pending.PrepareCursor < pending.WaterSource.Length)
                            {
                                pending.WaterSource[pending.PrepareCursor] =
                                    pending.Water.Vertices[pending.PrepareCursor];
                                pending.PrepareCursor++;
                                iterations++;
                                if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                    budgetMilliseconds)
                                {
                                    operationHealthRev35PrepareSourceYields++;
                                    return false;
                                }
                            }
                        }
                        pending.PrepareSubstage = 3;
                        pending.PrepareCursor = 0;
                        break;
                    case 3:
                        while (pending.PrepareCursor < pending.LandElevation.Length)
                        {
                            pending.LandElevation[pending.PrepareCursor] =
                                pending.Land.Elevation[pending.PrepareCursor];
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PrepareSourceYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 4;
                        pending.PrepareCursor = 0;
                        break;
                    case 4:
                        while (pending.PrepareCursor < pending.LandShade.Length)
                        {
                            pending.LandShade[pending.PrepareCursor] =
                                pending.Land.Shade[pending.PrepareCursor];
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PrepareSourceYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 5;
                        pending.PrepareCursor = 0;
                        break;
                    case 5:
                        pending.CoastalLandSource = BuildTriangleSourceVertices(
                            pending.Result.CoastalLandCorrectionVertices);
                        pending.PrepareSubstage = 6;
                        if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                            budgetMilliseconds)
                        {
                            operationHealthRev35PrepareSourceYields++;
                            return false;
                        }
                        break;
                    case 6:
                        pending.CoastalWaterSource = BuildTriangleSourceVertices(
                            pending.Result.CoastalWaterCorrectionVertices);
                        pending.PrepareSubstage = 7;
                        operationHealthSurfaceBuilderReuses++;
                        return true;
                    default:
                        return true;
                }
            }
        }
'''
renderer = replace_method(
    renderer,
    'void PreparePendingSources(PendingEntryCommit pending)',
    'void PreparePendingPackedTerrain(PendingEntryCommit pending)',
    sources_method,
    'PreparePendingSources')

packed_method = r'''        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,
            double budgetMilliseconds)
        {
            Vector3[] waterSource = pending.WaterSource;
            Vector3[] landSource = pending.LandSource;
            Vector3[] coastalWaterSource = pending.CoastalWaterSource;
            Vector3[] coastalLandSource = pending.CoastalLandSource;
            Color32 waterColour = ResolveWaterColour(AERISTerrainColourPreset.Standard);
            Color32 landColour = new Color32(255, 255, 255, 255);
            while (true)
            {
                int iterations = 0;
                switch (pending.PrepareSubstage)
                {
                    case 0:
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
                        pending.PackedSource = new Vector3[vertexCount];
                        pending.PackedColours = new Color32[vertexCount];
                        pending.PackedIndices = new int[indexCount];
                        pending.PrepareSubstage = 1;
                        pending.PrepareCursor = 0;
                        pending.PackedIndexWriteCursor = 0;
                        if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                            budgetMilliseconds)
                        {
                            operationHealthRev35PreparePackedYields++;
                            return false;
                        }
                        break;
                    case 1:
                        while (pending.PrepareCursor < pending.PackedWaterCount)
                        {
                            int dst = pending.PackedWaterOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] = waterSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = waterColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 2;
                        pending.PrepareCursor = 0;
                        break;
                    case 2:
                        while (pending.PrepareCursor < pending.PackedLandCount)
                        {
                            int dst = pending.PackedLandOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] = landSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = landColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 3;
                        pending.PrepareCursor = 0;
                        break;
                    case 3:
                        while (pending.PrepareCursor < pending.PackedCoastalWaterCount)
                        {
                            int dst = pending.PackedCoastalWaterOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] =
                                coastalWaterSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = waterColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 4;
                        pending.PrepareCursor = 0;
                        break;
                    case 4:
                        while (pending.PrepareCursor < pending.PackedCoastalLandCount)
                        {
                            int dst = pending.PackedCoastalLandOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] =
                                coastalLandSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = landColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 5;
                        pending.PrepareCursor = 0;
                        break;
                    case 5:
                        while (pending.PrepareCursor < pending.Water.Triangles.Count)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedWaterOffset +
                                pending.Water.Triangles[pending.PrepareCursor++];
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 6;
                        pending.PrepareCursor = 0;
                        break;
                    case 6:
                        while (pending.PrepareCursor < pending.Land.Triangles.Count)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedLandOffset +
                                pending.Land.Triangles[pending.PrepareCursor++];
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 7;
                        pending.PrepareCursor = 0;
                        break;
                    case 7:
                        while (pending.PrepareCursor < pending.PackedCoastalWaterCount)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedCoastalWaterOffset + pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 8;
                        pending.PrepareCursor = 0;
                        break;
                    case 8:
                        while (pending.PrepareCursor < pending.PackedCoastalLandCount)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedCoastalLandOffset + pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % Rev35PrepareChunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 9;
                        return true;
                    default:
                        return true;
                }
            }
        }
'''
renderer = replace_method(
    renderer,
    'void PreparePendingPackedTerrain(PendingEntryCommit pending)',
    'Mesh UploadPreparedPackedTerrainMesh(string name, PendingEntryCommit pending)',
    packed_method,
    'PreparePendingPackedTerrain')

renderer, _ = replace_once(
    renderer,
    '                "; oh_main_commit_publish=" + operationHealthMainCommitPublishes +\n',
    '                "; oh_main_commit_publish=" + operationHealthMainCommitPublishes +\n'
    '                "; oh_rev35_variant=" + Rev35Variant +\n'
    '                "; oh_rev35_prepare_source_yield=" + operationHealthRev35PrepareSourceYields +\n'
    '                "; oh_rev35_prepare_packed_yield=" + operationHealthRev35PreparePackedYields +\n',
    'REV3.5 telemetry append')

if 'REV3_5_VARIANT="' + MARKER + '"' not in build:
    build, _ = replace_once(
        build,
        'OBSERVER_VARIANT="AERIS26_REV003_OBSERVER_M1"\n',
        'OBSERVER_VARIANT="AERIS26_REV003_OBSERVER_M1"\n'
        'REV3_5_VARIANT="' + MARKER + '"\n',
        'build REV3.5 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris26_rev003_observer.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris26_rev003_observer.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py"\n',
        'build REV3.5 verifier')
    build, _ = replace_once(
        build,
        'printf \'observer_variant=%s\\n\' "$OBSERVER_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'observer_variant=%s\\n\' "$OBSERVER_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_variant=%s\\n\' "$REV3_5_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate REV3.5 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('behavior_base=NOREPINEPHRINE_OH_PHASE6_003')
print('observer=AERIS26_REV003_OBSERVER_M1')
print('rev3_5_variant=' + MARKER)
print('change=MAIN_THREAD_MANAGED_PREPARE_RESUMABLE')
print('worker_prepare=0 speculative=0 presentation_cache=0')
