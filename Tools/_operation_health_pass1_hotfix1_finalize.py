#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[1]

build=root/'build_ubuntu.sh'
s=build.read_text(encoding='utf-8')
s=s.replace('DEV CP3.75 OPERATION HEALTH PASS 1"','DEV CP3.75 OPERATION HEALTH PASS 1 HOTFIX 1"')
s=s.replace('DEV CP3.75 — OPERATION HEALTH PASS 1"','DEV CP3.75 — OPERATION HEALTH PASS 1 HOTFIX 1"')
if 'DEV CP3.75 OPERATION HEALTH PASS 1 HOTFIX 1' not in s or 'DEV CP3.75 — OPERATION HEALTH PASS 1 HOTFIX 1' not in s:
    raise SystemExit('build identity replacement failed')
build.write_text(s,encoding='utf-8')

gen=root/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
g=gen.read_text(encoding='utf-8')
g=g.replace('DEV CP3.75 OPERATION HEALTH PASS 1"','DEV CP3.75 OPERATION HEALTH PASS 1 HOTFIX 1"')
g=g.replace('DEV CP3.75 — OPERATION HEALTH PASS 1"','DEV CP3.75 — OPERATION HEALTH PASS 1 HOTFIX 1"')
if 'HOTFIX 1' not in g:
    raise SystemExit('generated identity replacement failed')
gen.write_text(g,encoding='utf-8')
print('Operation Health Pass 1 Hotfix 1 identity finalized')
