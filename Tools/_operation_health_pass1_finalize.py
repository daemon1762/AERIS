#!/usr/bin/env python3
from pathlib import Path
p=Path('build_ubuntu.sh')
s=p.read_text(encoding='utf-8')
repls=[
('DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 COASTAL CONTOUR TOPOLOGY CANDIDATE 10"','DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH PASS 1"'),
('internal const string UiCheckpoint = "DEV CP3.75 — COASTAL CONTOUR TOPOLOGY CANDIDATE 10"','internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PASS 1"'),
('Tools/run_v01800_cp375_candidate10_prebuild.py','Tools/run_v01800_operation_health_pass1_prebuild.py'),
]
# Current branch may already have Candidate11 build entrypoint rather than Candidate10.
alt=[
('DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 CONTOUR LEVEL BUDGET COASTAL CLIP CANDIDATE 11"','DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH PASS 1"'),
('internal const string UiCheckpoint = "DEV CP3.75 — CONTOUR LEVEL BUDGET / COASTAL CLIP CANDIDATE 11"','internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PASS 1"'),
('Tools/run_v01800_cp375_candidate11_prebuild.py','Tools/run_v01800_operation_health_pass1_prebuild.py'),
]
for old,new in repls+alt:
    if old in s:
        s=s.replace(old,new)
if 'DEV CP3.75 OPERATION HEALTH PASS 1' not in s:
    raise SystemExit('Operation Health display replacement failed')
if 'run_v01800_operation_health_pass1_prebuild.py' not in s:
    raise SystemExit('Operation Health prebuild replacement failed')
p.write_text(s,encoding='utf-8')
print('Operation Health Pass 1 build entrypoint finalized')
