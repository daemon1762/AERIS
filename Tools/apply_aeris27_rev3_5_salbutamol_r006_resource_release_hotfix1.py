#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 RESOURCE RELEASE HOTFIX1]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'


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
if R006 not in renderer:
    fail('R006 generated parent required')
if HF1 in renderer:
    print(PREFIX + ' already present')
    raise SystemExit(0)

renderer, _ = replace_once(
    renderer,
    '        const string Rev35R006Variant = "' + R006 + '";\n',
    '        const string Rev35R006Variant = "' + R006 + '";\n'
    '        const string Rev35R006ResourceReleaseHotfix1 = "' + HF1 + '";\n',
    'R006 HF1 identity')

# Full terrain OFF/suspend/dispose already destroys the native Mesh recycle pool.
# Mirror that existing lifetime boundary for the new managed geographic pool. Ordinary
# Entry eviction still keeps the bounded pool hot for reuse.
renderer, _ = replace_once(
    renderer,
    '            DestroyMeshPool();\n            identityIndexCache.Clear();\n',
    '            DestroyMeshPool();\n'
    '            ClearRev35R006GeographicPool();\n'
    '            identityIndexCache.Clear();\n',
    'full resource release clears R006 managed pool')

if 'REV3_5_R006_HOTFIX1="' + HF1 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R006_VARIANT="' + R006 + '"\n',
        'REV3_5_R006_VARIANT="' + R006 + '"\n'
        'REV3_5_R006_HOTFIX1="' + HF1 + '"\n',
        'build HF1 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py"\n',
        'build HF1 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r006_variant=%s\\n\' "$REV3_5_R006_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r006_variant=%s\\n\' "$REV3_5_R006_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r006_hotfix1=%s\\n\' "$REV3_5_R006_HOTFIX1" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate HF1 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('hotfix=' + HF1)
print('ordinary_retirement=POOL_REUSE')
print('terrain_off_suspend_dispose=POOL_CLEAR')
