#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
M = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
C = ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg'
U = ROOT / 'build_ubuntu.sh'
P5V = ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'
# Keep the canonical unified diff in small exact fragments. Large manually copied
# fragments previously lost context lines and produced a malformed patch in CI.
PARTS = [ROOT / ('Tools/aeris25_phase6_002_staged_commit.patch.s%02d' % i)
         for i in range(13)]
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_002]'
MARKER = 'AERIS25_STAGED_MAIN_THREAD_COMMIT'


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch old=%d' % (PREFIX, label, count))
    return text.replace(old, new, 1), True


def apply_renderer_patch():
    renderer = R.read_text()
    if MARKER in renderer:
        print(PREFIX + ' staged renderer patch already present')
        return False
    if 'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR' not in renderer or \
       'OH_PHASE6_001' not in M.read_text():
        raise SystemExit(PREFIX + ' Phase6_001 generated parent is required')
    missing = [str(p) for p in PARTS if not p.is_file()]
    if missing:
        raise SystemExit(PREFIX + ' patch fragment missing: ' + ', '.join(missing))
    patch_text = ''.join(p.read_text() for p in PARTS)
    proc = subprocess.run(['patch', '--batch', '--forward', '-p0'], cwd=str(ROOT),
                          input=patch_text, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(proc.stdout, end='')
    if proc.returncode != 0:
        raise SystemExit(PREFIX + ' renderer staged patch failed')
    renderer = R.read_text()
    if MARKER not in renderer:
        raise SystemExit(PREFIX + ' renderer marker missing after patch')
    print(PREFIX + ' staged renderer patch applied')
    return True


apply_renderer_patch()

monitor = M.read_text()
monitor, m1 = replace_once(monitor,
    'internal const string Revision = "OH_PHASE6_001";',
    'internal const string Revision = "OH_PHASE6_002";', 'revision identity')
if 'internal const string Codename = "NOREPINEPHRINE";' not in monitor or \
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' not in monitor:
    raise SystemExit(PREFIX + ' NOREPINEPHRINE candidate identity was not inherited')
if m1:
    M.write_text(monitor)

config = C.read_text()
if 'codename = NOREPINEPHRINE' not in config:
    if 'codename = ADENOSINE' in config:
        config = config.replace('codename = ADENOSINE', 'codename = NOREPINEPHRINE', 1)
    elif 'codename = ATROPINE' in config:
        config = config.replace('codename = ATROPINE', 'codename = NOREPINEPHRINE', 1)
    else:
        raise SystemExit(PREFIX + ' Operation Health config codename mismatch')
    C.write_text(config)

build = U.read_text()
build, b1 = replace_once(build,
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV001"',
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT"',
    'build display')
build, b2 = replace_once(build,
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV001',
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV002 STAGED COMMIT',
    'build checkpoint')
build, b3 = replace_once(build,
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_main_thread_commit_governor.py"',
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_staged_main_thread_commit_hotfix.py"',
    'active Phase6_002 verifier')
if any((b1, b2, b3)):
    U.write_text(build)

# The accepted ADENOSINE contract remains an inherited verifier. Admit only this exact
# NOREPINEPHRINE revision as a descendant and the exact new final-tree verifier name.
p5v = P5V.read_text()
old_rev = '''phase6_identity = ('internal const string Codename = "NOREPINEPHRINE";' in M and
    'internal const string Revision = "OH_PHASE6_001";' in M and
    'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
    'codename = NOREPINEPHRINE' in C)'''
new_rev = '''phase6_identity = ('internal const string Codename = "NOREPINEPHRINE";' in M and
    (('internal const string Revision = "OH_PHASE6_001";' in M) or
     ('internal const string Revision = "OH_PHASE6_002";' in M)) and
    'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
    'codename = NOREPINEPHRINE' in C)'''
p5v, p1 = replace_once(p5v, old_rev, new_rev, 'Phase5 descendant revision')
old_build = '''phase6_build = ('CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"' in U and
    'OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV001' in U and
    'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV001' in U)'''
new_build = '''phase6_build = ('CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"' in U and
    (('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV001' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV001' in U) or
     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV002 STAGED COMMIT' in U)))'''
p5v, p2 = replace_once(p5v, old_build, new_build, 'Phase5 descendant build')
old_active = '''ck((('verify_aeris25_persistent_presentation_batching.py' in active) or
    ('verify_aeris25_main_thread_commit_governor.py' in active)) and'''
new_active = '''ck((('verify_aeris25_persistent_presentation_batching.py' in active) or
    ('verify_aeris25_main_thread_commit_governor.py' in active) or
    ('verify_aeris25_staged_main_thread_commit_hotfix.py' in active)) and'''
p5v, p3 = replace_once(p5v, old_active, new_active, 'Phase5 descendant verifier')
if any((p1, p2, p3)):
    P5V.write_text(p5v)

print(PREFIX + ' STAGED MAIN THREAD COMMIT APPLIED')
print('Design: non-authoritative Repaint may advance only bounded staged commit work; visible projection authority remains fixed 10 Hz')
print('Stages: clip -> source/pack -> terrain/contour/coast upload -> geographic chunks -> final publish')
print('Invariant: no partial Entry is added to presentation authority before Finalize')
