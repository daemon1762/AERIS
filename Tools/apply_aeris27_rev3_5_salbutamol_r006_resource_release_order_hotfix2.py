#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 RESOURCE RELEASE ORDER HOTFIX2]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
HF2 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'


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
if R006 not in renderer or HF1 not in renderer:
    fail('R006 + HF1 generated parent required')
if HF2 in renderer:
    print(PREFIX + ' already present')
    raise SystemExit(0)

renderer, _ = replace_once(
    renderer,
    '        const string Rev35R006ResourceReleaseHotfix1 = "' + HF1 + '";\n',
    '        const string Rev35R006ResourceReleaseHotfix1 = "' + HF1 + '";\n'
    '        const string Rev35R006ResourceReleaseOrderHotfix2 = "' + HF2 + '";\n',
    'R006 HF2 identity')

# Phase6_002/003 ResetContentSnapshot may cancel the active staged commit and may force
# release deferred presentation Entries. Both operations can recycle native Meshes, and
# R006 now also recycles bare geographic arrays. Perform a final pool drain AFTER reset so
# full terrain OFF/suspend/dispose cannot be repopulated by that teardown itself.
renderer, _ = replace_once(
    renderer,
    '            ResetContentSnapshot();\n            DestroyRenderTargets();\n',
    '            ResetContentSnapshot();\n'
    '            // R006 HF2: teardown above can recycle pending/deferred resources.\n'
    '            // Drain both pools once more after reset; ordinary eviction still reuses.\n'
    '            DestroyMeshPool();\n'
    '            ClearRev35R006GeographicPool();\n'
    '            DestroyRenderTargets();\n',
    'post-reset final pool drain')

if 'REV3_5_R006_HOTFIX2="' + HF2 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R006_HOTFIX1="' + HF1 + '"\n',
        'REV3_5_R006_HOTFIX1="' + HF1 + '"\n'
        'REV3_5_R006_HOTFIX2="' + HF2 + '"\n',
        'build HF2 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py"\n',
        'build HF2 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r006_hotfix1=%s\\n\' "$REV3_5_R006_HOTFIX1" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r006_hotfix1=%s\\n\' "$REV3_5_R006_HOTFIX1" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r006_hotfix2=%s\\n\' "$REV3_5_R006_HOTFIX2" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate HF2 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('hotfix=' + HF2)
print('teardown_order=RESET_CONTENT_SNAPSHOT -> FINAL_DESTROY_MESH_POOL -> FINAL_CLEAR_GEO_POOL')
