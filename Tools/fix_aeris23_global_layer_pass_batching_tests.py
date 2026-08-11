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

# Report any remaining legacy DrawEntry dependency in permanent selftests so the
# next prebuild failure is not discovered one suite at a time.
remaining = []
for path in sorted((ROOT / 'Tools').glob('selftest_*.py')):
    text = path.read_text()
    if "DrawEntry(" in text:
        remaining.append(path.name)

if remaining:
    print('[WARN] remaining selftests containing DrawEntry(: ' + ', '.join(remaining))
else:
    print('[PASS] no remaining permanent selftest depends on legacy DrawEntry(')

print('[AERIS23 Global Layer Pass Batching] legacy culling selftests updated')
