#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R004 ADAPTIVE HIGH FLOW COMMIT]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'


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
for required in (R001, R002, R003):
    if required not in renderer:
        fail('R003 generated parent required: ' + required)
if R004 in renderer:
    print(PREFIX + ' patch already present')
    sys.exit(0)

renderer, _ = replace_once(
    renderer,
    '        const string Rev35R003Variant = "' + R003 + '";\n',
    '        const string Rev35R003Variant = "' + R003 + '";\n'
    '        const string Rev35R004Variant = "' + R004 + '";\n'
    '        const double Rev35R004BudgetOneMilliseconds = 1.00;\n'
    '        const double Rev35R004BudgetOneHalfMilliseconds = 1.50;\n'
    '        const double Rev35R004BudgetMaximumMilliseconds = 2.00;\n'
    '        const double Rev35R004FrameGuardMediumMilliseconds = 15.0;\n'
    '        const double Rev35R004FrameGuardSoftMilliseconds = 20.0;\n'
    '        const double Rev35R004FrameGuardHardMilliseconds = 25.0;\n'
    '        const int Rev35R004PrepareChunkMedium = 128;\n'
    '        const int Rev35R004PrepareChunkHigh = 256;\n',
    'R004 identity/bounds')

renderer, _ = replace_once(
    renderer,
    '        long operationHealthRev35R003RelevantAdmissions;\n',
    '        long operationHealthRev35R003RelevantAdmissions;\n'
    '        long operationHealthRev35R004Budget050;\n'
    '        long operationHealthRev35R004Budget100;\n'
    '        long operationHealthRev35R004Budget150;\n'
    '        long operationHealthRev35R004Budget200;\n'
    '        long operationHealthRev35R004FrameGuard;\n'
    '        long operationHealthRev35R004AllocationContinues;\n'
    '        double operationHealthRev35R004BudgetMaxMs;\n'
    '        int operationHealthRev35R004ChunkMaxItems;\n',
    'R004 telemetry fields')

helper = '''        double ResolveRev35R004CommitBudget(bool steadyCommitProfile)\n        {\n            int backlog = Math.Max(0, rasterizer.CompletedCount) +\n                (pendingEntryCommit == null ? 0 : 1);\n            long generationLag = 0L;\n            if (contentTerrainGeneration >= 0L && frontTerrainGeneration >= 0L)\n                generationLag = Math.Max(0L,\n                    contentTerrainGeneration - frontTerrainGeneration);\n\n            double requestedBudget = steadyCommitProfile ?\n                MainThreadCommitSteadyBudgetMilliseconds :\n                MainThreadCommitBootstrapBudgetMilliseconds;\n            if (backlog >= 24 || generationLag >= 8L)\n                requestedBudget = Rev35R004BudgetMaximumMilliseconds;\n            else if (backlog >= 12 || generationLag >= 4L)\n                requestedBudget = Math.Max(requestedBudget,\n                    Rev35R004BudgetOneHalfMilliseconds);\n            else if (backlog >= 4 || generationLag >= 2L)\n                requestedBudget = Math.Max(requestedBudget,\n                    Rev35R004BudgetOneMilliseconds);\n\n            // Real unscaled Unity frame time is only a protective ceiling.\n            double frameMilliseconds = Math.Max(0.0,\n                Time.unscaledDeltaTime * 1000.0);\n            double frameCap = Rev35R004BudgetMaximumMilliseconds;\n            if (frameMilliseconds >= Rev35R004FrameGuardHardMilliseconds)\n                frameCap = MainThreadCommitSteadyBudgetMilliseconds;\n            else if (frameMilliseconds >= Rev35R004FrameGuardSoftMilliseconds)\n                frameCap = Rev35R004BudgetOneMilliseconds;\n            else if (frameMilliseconds >= Rev35R004FrameGuardMediumMilliseconds)\n                frameCap = Rev35R004BudgetOneHalfMilliseconds;\n            if (frameCap < requestedBudget)\n                operationHealthRev35R004FrameGuard++;\n\n            double selected = Math.Max(MainThreadCommitSteadyBudgetMilliseconds,\n                Math.Min(requestedBudget, frameCap));\n            if (selected >= 1.75)\n                operationHealthRev35R004Budget200++;\n            else if (selected >= 1.25)\n                operationHealthRev35R004Budget150++;\n            else if (selected >= 0.75)\n                operationHealthRev35R004Budget100++;\n            else\n                operationHealthRev35R004Budget050++;\n            operationHealthRev35R004BudgetMaxMs = Math.Max(\n                operationHealthRev35R004BudgetMaxMs, selected);\n            return selected;\n        }\n\n        int ResolveRev35R004PrepareChunkItems(double budgetMilliseconds)\n        {\n            int chunkItems = Rev35PrepareChunkItems;\n            if (budgetMilliseconds >= 1.75)\n                chunkItems = Rev35R004PrepareChunkHigh;\n            else if (budgetMilliseconds >= 1.00)\n                chunkItems = Rev35R004PrepareChunkMedium;\n            operationHealthRev35R004ChunkMaxItems = Math.Max(\n                operationHealthRev35R004ChunkMaxItems, chunkItems);\n            return chunkItems;\n        }\n\n'''

pump_sig = '        void PumpStagedCompletedCommit(AERISTerrainTileSystem system)\n'
renderer, _ = replace_once(renderer, pump_sig, helper + pump_sig,
                           'R004 adaptive helpers')

budget_old = '''            double budgetMilliseconds = steadyCommitProfile ?\n                MainThreadCommitSteadyBudgetMilliseconds :\n                MainThreadCommitBootstrapBudgetMilliseconds;\n            operationHealthMainCommitBudgetMilliseconds = budgetMilliseconds;\n'''
budget_new = '''            double budgetMilliseconds =\n                ResolveRev35R004CommitBudget(steadyCommitProfile);\n            operationHealthMainCommitBudgetMilliseconds = budgetMilliseconds;\n'''
renderer, _ = replace_once(renderer, budget_old, budget_new,
                           'R004 adaptive pump budget')

s0, s1, sources = method_slice(
    renderer,
    '        bool AdvancePendingSources(PendingEntryCommit pending,\n',
    '        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,\n')
sources, _ = replace_once(
    sources,
    '        {\n            while (true)\n',
    '        {\n            int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);\n            while (true)\n',
    'R004 source chunk selector')
count_sources = sources.count('(iterations % Rev35PrepareChunkItems) == 0')
if count_sources <= 0:
    fail('R004 source fixed-chunk witnesses missing')
sources = sources.replace('(iterations % Rev35PrepareChunkItems) == 0',
                          '(iterations % chunkItems) == 0')
renderer = renderer[:s0] + sources + renderer[s1:]

p0, p1, packed = method_slice(
    renderer,
    '        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,\n',
    '        Mesh UploadPreparedPackedTerrainMesh(')
packed, _ = replace_once(
    packed,
    '        {\n            Vector3[] waterSource = pending.WaterSource;\n',
    '        {\n            int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);\n            Vector3[] waterSource = pending.WaterSource;\n',
    'R004 packed chunk selector')
count_packed = packed.count('(iterations % Rev35PrepareChunkItems) == 0')
if count_packed <= 0:
    fail('R004 packed fixed-chunk witnesses missing')
packed = packed.replace('(iterations % Rev35PrepareChunkItems) == 0',
                        '(iterations % chunkItems) == 0')

for substage in (2, 3, 4):
    old = ('                            pending.PrepareSubstage = %d;\n'
           '                            operationHealthRev35PreparePackedYields++;\n'
           '                            return false;\n') % substage
    new = ('                            pending.PrepareSubstage = %d;\n'
           '                            if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=\n'
           '                                budgetMilliseconds)\n'
           '                            {\n'
           '                                operationHealthRev35PreparePackedYields++;\n'
           '                                return false;\n'
           '                            }\n'
           '                            operationHealthRev35R004AllocationContinues++;\n'
           '                            break;\n') % substage
    if old not in packed:
        fail('R004 allocation-yield anchor missing for substage %d' % substage)
    packed = packed.replace(old, new, 1)
renderer = renderer[:p0] + packed + renderer[p1:]

renderer, _ = replace_once(
    renderer,
    '                "; oh_rev35_r003_relevant_admit=" + operationHealthRev35R003RelevantAdmissions +\n',
    '                "; oh_rev35_r003_relevant_admit=" + operationHealthRev35R003RelevantAdmissions +\n'
    '                "; oh_rev35_r004_variant=" + Rev35R004Variant +\n'
    '                "; oh_rev35_r004_budget_050=" + operationHealthRev35R004Budget050 +\n'
    '                "; oh_rev35_r004_budget_100=" + operationHealthRev35R004Budget100 +\n'
    '                "; oh_rev35_r004_budget_150=" + operationHealthRev35R004Budget150 +\n'
    '                "; oh_rev35_r004_budget_200=" + operationHealthRev35R004Budget200 +\n'
    '                "; oh_rev35_r004_frame_guard=" + operationHealthRev35R004FrameGuard +\n'
    '                "; oh_rev35_r004_alloc_continue=" + operationHealthRev35R004AllocationContinues +\n'
    '                "; oh_rev35_r004_budget_max_ms=" + operationHealthRev35R004BudgetMaxMs.ToString("F2", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r004_chunk_max_items=" + operationHealthRev35R004ChunkMaxItems +\n',
    'R004 telemetry append')

if 'REV3_5_R004_VARIANT="' + R004 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R003_VARIANT="' + R003 + '"\n',
        'REV3_5_R003_VARIANT="' + R003 + '"\n'
        'REV3_5_R004_VARIANT="' + R004 + '"\n',
        'build R004 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py"\n',
        'build R004 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r003_variant=%s\\n\' "$REV3_5_R003_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r003_variant=%s\\n\' "$REV3_5_R003_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r004_variant=%s\\n\' "$REV3_5_R004_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R004 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R003)
print('r004=' + R004)
print('budget_ms=0.50/1.00/1.50/2.00 adaptive; frame_guard=15/20/25ms')
print('chunk_items=64/128/256 adaptive')
print('r002_alloc_yield=BUDGET_AWARE')
print('worker_count_change=0 speculative=0 presentation_cache=0 quality_change=0 authority_change=0')
