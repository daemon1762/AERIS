#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[1]

renderer=root/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
s=renderer.read_text(encoding='utf-8')
if 'const int MaximumPooledMeshes = 96;' in s:
    s=s.replace('const int MaximumPooledMeshes = 96;',
                'const int MaximumPooledMeshes = 24;',1)
if 'const int MaximumPooledMeshes = 24;' not in s:
    raise SystemExit('bounded mesh-pool identity missing')
renderer.write_text(s,encoding='utf-8')

build=root/'build_ubuntu.sh'
b=build.read_text(encoding='utf-8')
b=b.replace('DEV CP3.75 OPERATION HEALTH PASS 1 HOTFIX 1"',
            'DEV CP3.75 OPERATION HEALTH PASS 2"')
b=b.replace('DEV CP3.75 — OPERATION HEALTH PASS 1 HOTFIX 1"',
            'DEV CP3.75 — OPERATION HEALTH PASS 2"')
b=b.replace('run_v01800_operation_health_pass1_prebuild.py',
            'run_v01800_operation_health_pass2_prebuild.py')
if 'DEV CP3.75 OPERATION HEALTH PASS 2' not in b:
    raise SystemExit('Pass 2 DISPLAY replacement failed')
if 'DEV CP3.75 — OPERATION HEALTH PASS 2' not in b:
    raise SystemExit('Pass 2 UiCheckpoint replacement failed')
if 'run_v01800_operation_health_pass2_prebuild.py' not in b:
    raise SystemExit('Pass 2 prebuild replacement failed')
build.write_text(b,encoding='utf-8')

print('Operation Health Pass 2 finalization applied')
