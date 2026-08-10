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

hotfix3 = ROOT / 'Tools/selftest_v01800_operation_health_pass3_cadence_hotfix3_motion_commit.py'
replace_once(
    hotfix3,
    "project=R[R.index('void EnsureProjectedGeometry('):R.index('void ProjectMesh(')]",
    "project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(')]",
    'Hotfix3 projection method anchor')

pass2 = ROOT / 'Tools/selftest_v01800_operation_health_pass2_persistent_geometry.py'
replace_once(
    pass2,
    "check('projection geometry still updates only when dirty', 'if (!projectionChanged) return;' in renderer and 'EnsureProjectedGeometry' in renderer)",
    "check('projection geometry still updates only when exact-dirty while subpixel motion avoids vertex rewrite',\n      'bool exactProjectionDue =' in renderer and 'if (!exactProjectionDue)' in renderer and\n      'Matrix4x4.Translate' in renderer and\n      renderer.index('if (!exactProjectionDue)') < renderer.index('ProjectMesh(entry.LandMesh'))",
    'Pass2 exact-dirty projection contract')

print('[AERIS23 Projection Motion Bridge] legacy selftests updated')
