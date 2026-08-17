#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
BASE_GENERATOR = ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_resumable_prepare.py'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001 COMPILE HOTFIX1]'
MARKER = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
BAD_YIELD = 'return YieldPendingEntryCommit(stageStart, true);'
GOOD_YIELD = 'return YieldPendingEntryCommit(executedStage, stageStart, true);'
LEGACY_NEXT = 'PendingEntryCommitStage.UploadPackedTerrain'
REV003_NEXT = 'PendingEntryCommitStage.AcquirePackedTerrainMesh'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def patch_existing_generated_tree(renderer):
    changed = False
    bad_count = renderer.count(BAD_YIELD)
    if bad_count not in (0, 2):
        fail('unexpected two-argument R001 yield count=%d' % bad_count)
    if bad_count == 2:
        renderer = renderer.replace(BAD_YIELD, GOOD_YIELD)
        changed = True

    # A short-lived early R001 generator used the pre-REV003 successor stage name.
    # Normalize only the exact generated stage transition if it is present.
    legacy_transition = 'pending.Stage = ' + LEGACY_NEXT + ';'
    rev003_transition = 'pending.Stage = ' + REV003_NEXT + ';'
    legacy_count = renderer.count(legacy_transition)
    if legacy_count > 1:
        fail('unexpected legacy packed successor count=%d' % legacy_count)
    if legacy_count == 1:
        renderer = renderer.replace(legacy_transition, rev003_transition, 1)
        changed = True

    if BAD_YIELD in renderer:
        fail('two-argument YieldPendingEntryCommit survived repair')
    if rev003_transition not in renderer:
        fail('REV003 AcquirePackedTerrainMesh successor missing after repair')
    return renderer, changed


def run_corrected_generator_in_memory():
    if not BASE_GENERATOR.is_file():
        fail('base R001 generator missing')
    source = BASE_GENERATOR.read_text()
    if BAD_YIELD not in source:
        fail('base generator two-argument yield witness missing')
    if LEGACY_NEXT not in source:
        fail('base generator legacy successor witness missing')

    corrected = source.replace(BAD_YIELD, GOOD_YIELD)
    corrected = corrected.replace(LEGACY_NEXT, REV003_NEXT)
    if corrected == source:
        fail('generator correction produced no change')

    namespace = {
        '__name__': '__main__',
        '__file__': str(BASE_GENERATOR),
        '__package__': None,
    }
    code = compile(corrected, str(BASE_GENERATOR) + '<COMPILE_HOTFIX1>', 'exec')
    exec(code, namespace, namespace)


if not R.is_file():
    fail('generated renderer missing')

renderer = R.read_text()
if MARKER not in renderer:
    print(PREFIX + ' clean REV003 parent detected; executing corrected R001 generator in memory')
    run_corrected_generator_in_memory()
    renderer = R.read_text()
    if MARKER not in renderer:
        fail('R001 marker missing after corrected generator')
else:
    print(PREFIX + ' existing R001 generated tree detected; repairing compile contract in place')

renderer, changed = patch_existing_generated_tree(renderer)
if changed:
    R.write_text(renderer)
    print(PREFIX + ' generated renderer repaired')
else:
    print(PREFIX + ' generated renderer already compile-correct')

renderer = R.read_text()
# Scope the check to the two R001 resumable stage dispatches. This would have caught
# the desktop CS1501 before a real KSP build.
for stage_name, advance_name in (
    ('PrepareSources', 'AdvancePendingSources'),
    ('PreparePackedTerrain', 'AdvancePendingPackedTerrain'),
):
    pattern = re.compile(
        r'case PendingEntryCommitStage\.' + re.escape(stage_name) +
        r':.*?if \(!' + re.escape(advance_name) +
        r'\(pending, budgetMilliseconds\)\).*?return YieldPendingEntryCommit\('
        r'executedStage, stageStart, true\);',
        re.S)
    if not pattern.search(renderer):
        fail(stage_name + ' does not use REV003 three-argument yield contract')

if 'case PendingEntryCommitStage.PreparePackedTerrain:' not in renderer or \
   'pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;' not in renderer:
    fail('PreparePackedTerrain does not preserve REV003 AcquirePackedTerrainMesh successor')

print(PREFIX + ' PASS')
print('fix=YieldPendingEntryCommit_REV003_3ARG + AcquirePackedTerrainMesh successor')
print('scope=compile-contract-only; performance/visual/authority semantics unchanged')
