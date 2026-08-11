#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text()
    if new in text:
        print(f'[PASS] {label} already fixed')
        return
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 old anchor, found {count}')
    path.write_text(text.replace(old, new, 1))
    print(f'[PASS] {label} fixed')

conservative = ROOT / 'Tools/selftest_v01800_operation_health_conservative_entry_culling.py'
replace_once(
    conservative,
    "pos_draw=render.index('DrawEntry(')\nck(pos_cull < pos_project < pos_draw,'culling occurs before projection, mesh upload and draw')",
    "pos_draw=render.index('DrawLayerBatches(')\nck(pos_cull < pos_project < pos_draw,'culling occurs before projection, mesh upload and global layer draw')",
    'Conservative culling draw-stage anchor')

dotcap = ROOT / 'Tools/selftest_v01800_operation_health_dot_cap_culling.py'
replace_once(
    dotcap,
    "ck(render.index('ShouldCullEntryOutsidePresentation') < render.index('DrawEntry'), 'cull remains before draw submission')",
    "ck(render.index('ShouldCullEntryOutsidePresentation') < render.index('DrawLayerBatches'), 'cull remains before global layer draw submission')",
    'Dot-cap draw-stage anchor')

hotfix3 = ROOT / 'Tools/selftest_v01800_operation_health_pass3_cadence_hotfix3_motion_commit.py'
replace_once(
    hotfix3,
    "render=R[R.index('bool RenderBackBuffer('):R.index('bool DrawEntry(')]",
    "render=R[R.index('bool RenderBackBuffer('):R.index('float MeasureFoundationGpuReadiness(')]",
    'Hotfix3 RenderBackBuffer slice anchor')

cp2_runway = ROOT / 'Tools/selftest_v01800_cp2_runway_map_lock_hotfix1.py'
replace_once(
    cp2_runway,
    '    "DrawEntry(fallbackEntry, mapRotation", "DrawEntry(currentEntry, mapRotation",\n',
    '    "Matrix4x4 entryMapMatrix = mapRotation * projectionBridge",\n    "layerBatchMatricesScratch[layerBatchCount] = entryMapMatrix",\n',
    'CP2 runway shared map-matrix draw contract')

gate4a_runway = ROOT / 'Tools/selftest_v01800_cp3_gate4a_runway_map_lock_successor.py'
replace_once(
    gate4a_runway,
    '    "DrawEntry(drawEntry, mapRotation",\n',
    '    "Matrix4x4 entryMapMatrix = mapRotation * projectionBridge",\n    "layerBatchMatricesScratch[layerBatchCount] = entryMapMatrix",\n',
    'Gate4A runway shared map-matrix draw contract')

remaining = []
for path in sorted((ROOT / 'Tools').glob('selftest_*.py')):
    text = path.read_text()
    if "DrawEntry(" in text:
        remaining.append(path.name)

if remaining:
    print('[WARN] remaining selftests containing DrawEntry(: ' + ', '.join(remaining))
else:
    print('[PASS] no remaining permanent selftest depends on legacy DrawEntry(')

print('[AERIS23 Global Layer Pass Batching] legacy draw-contract selftests updated')
