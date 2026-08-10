#!/usr/bin/env python3
from pathlib import Path

p = Path(__file__).resolve().parents[1] / 'Tools/selftest_v01800_operation_health_pass3_cadence_hotfix3_motion_commit.py'
s = p.read_text()
old = "project=R[R.index('void EnsureProjectedGeometry('):R.index('void ProjectMesh(')]"
new = "project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(')]"
if old in s:
    s = s.replace(old, new, 1)
elif new not in s:
    raise SystemExit('Hotfix3 projection slice anchor not found')
p.write_text(s)
print('[AERIS23 Projection Motion Bridge] Hotfix3 selftest anchor corrected')
