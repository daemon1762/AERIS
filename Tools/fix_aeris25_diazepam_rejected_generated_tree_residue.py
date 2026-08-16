#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS25 DIAZEPAM REV006 RESIDUE CLEANUP]'

# Exact tracked files observed to be rewritten by historical AERIS23/24/25 runtime
# preparers.  These are generated/reconstructed build inputs, not user-authored
# project documents.  Only paths in this allow-list may be restored automatically.
TRACKED_GENERATED = [
    'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg',
    'GameData/AERISFlightControl/Config/AERISSettings.cfg',
    'GpuAssets/Assets/AERISNdExactVertexProjection.shader',
    'GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs',
    'Source/AERISFlightControl/AERISFlightControl.csproj',
    'Source/AERISFlightControl/Core/AERISBootstrap.cs',
    'Source/AERISFlightControl/Logging/AERISLogger.cs',
    'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs',
    'Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs',
    'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs',
    'Source/AERISFlightControl/Settings/AERISSettings.cs',
    'Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
    'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs',
    'Source/AERISFlightControl/UI/AERISWindow.cs',
    'Tools/apply_aeris23_single_authority_terrain_pack_successor.py',
    'Tools/apply_aeris24_gpu_vertex_projection_poc.py',
    'Tools/apply_aeris24_nd_backend_reload.py',
    'Tools/apply_aeris25_gpu_dynamic_terrain_colour.py',
    'Tools/fix_aeris24_gpu_vertex_single_authority_selftest.py',
    'Tools/run_v01800_operation_health_pass3_prebuild.py',
    'Tools/selftest_v01800_operation_health_pass1_zero_visual_cost.py',
    'Tools/selftest_v01800_operation_health_pass2_persistent_geometry.py',
    'Tools/selftest_v01800_operation_health_pass3_projection_draw_reduction.py',
    'Tools/selftest_v01800_operation_health_projection_motion_bridge.py',
    'Tools/selftest_v01800_operation_health_retained_surface.py',
    'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py',
    'Tools/verify_aeris25_chunk_cull_guard_hotfix.py',
    'Tools/verify_aeris25_gpu_dynamic_terrain_colour.py',
    'Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py',
    'Tools/verify_aeris25_persistent_presentation_batching.py',
    'Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py',
    'build_ubuntu.sh',
]

# Exact untracked files emitted by the historical reconstruction chain.  Do not
# recursively clean directories: shader bundles / Unity Library are intentionally
# left intact so an accepted bundle can be reused and unrelated untracked work is safe.
UNTRACKED_GENERATED = [
    'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt',
    'Tools/selftest_v01800_operation_health_entry_terrain_mesh_packing.py',
    'Tools/selftest_v01800_operation_health_staggered_exact_refresh.py',
    'Tools/selftest_v01800_operation_health_witness_affine_projection.py',
]

EVIDENCE_FILES = [
    'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt',
    'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
    'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs',
    'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg',
]
EVIDENCE_MARKERS = (
    'OH_PHASE6_004',
    'OH_PHASE6_005',
    'MANAGED PREPARATION PIPELINE',
    'MANAGED_PREPARATION_PIPELINE',
    'NONBLOCKING SPECULATIVE PREPARATION',
    'NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR',
    'NOREPINEPHRINE',
)


def run(args, check=True, capture=False):
    kwargs = dict(cwd=str(ROOT), check=check)
    if capture:
        kwargs.update(stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    return subprocess.run([str(x) for x in args], **kwargs)


def tracked_dirty(path):
    return run(['git', 'diff', '--quiet', 'HEAD', '--', path], check=False).returncode != 0


def is_tracked(path):
    return run(['git', 'ls-files', '--error-unmatch', path], check=False, capture=True).returncode == 0


def read_text(path):
    try:
        return (ROOT / path).read_text(errors='replace')
    except OSError:
        return ''


def residue_evidence(dirty):
    if not dirty:
        return False
    identity = ROOT / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
    if identity.is_file() and not is_tracked('GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'):
        return True
    for path in UNTRACKED_GENERATED[1:]:
        p = ROOT / path
        if p.exists() and not is_tracked(path):
            return True
    for path in EVIDENCE_FILES:
        text = read_text(path)
        if any(marker in text for marker in EVIDENCE_MARKERS):
            return True
    return False


dirty = [path for path in TRACKED_GENERATED if tracked_dirty(path)]
if not dirty:
    print(PREFIX + ' no tracked generated residue; nothing to restore')
    raise SystemExit(0)

if not residue_evidence(dirty):
    print(PREFIX + ' REFUSE: generated allow-list files are dirty but no rejected/runtime-generated identity evidence was found.')
    print(PREFIX + ' This guard prevents overwriting an ambiguous local edit. Inspect git status before continuing.')
    for path in dirty:
        print('  DIRTY ' + path)
    raise SystemExit(3)

print(PREFIX + ' rejected/runtime-generated residue confirmed; restoring exact allow-list paths only')
for path in dirty:
    print('  RESTORE ' + path)
run(['git', 'restore', '--source=HEAD', '--'] + dirty)

for path in UNTRACKED_GENERATED:
    p = ROOT / path
    if p.exists() and not is_tracked(path):
        if p.is_file() or p.is_symlink():
            p.unlink()
            print('  REMOVE GENERATED ' + path)

remaining = [path for path in TRACKED_GENERATED if tracked_dirty(path)]
if remaining:
    print(PREFIX + ' FAIL: allow-list residue remains after targeted restore')
    for path in remaining:
        print('  REMAINS ' + path)
    raise SystemExit(4)

print(PREFIX + ' PASS: rejected/generated tracked residue cleared without reset --hard and without recursive clean')
