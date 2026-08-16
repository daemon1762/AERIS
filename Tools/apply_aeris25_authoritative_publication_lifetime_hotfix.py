#!/usr/bin/env python3
from pathlib import Path
import base64
import subprocess
import sys
import zlib

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
M = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
C = ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg'
U = ROOT / 'build_ubuntu.sh'
P5V = ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_003]'
MARKER = 'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION'
PATCH_ZLIB_B64 = """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"""


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
        print(PREFIX + ' authoritative publication renderer patch already present')
        return False
    monitor = M.read_text()
    if 'AERIS25_STAGED_MAIN_THREAD_COMMIT' not in renderer or \
       'internal const string Revision = "OH_PHASE6_002";' not in monitor:
        raise SystemExit(PREFIX + ' generated Phase6_002 parent is required')
    patch_text = zlib.decompress(base64.b64decode(PATCH_ZLIB_B64)).decode()
    proc = subprocess.run(['patch', '--batch', '--forward', '-p0'], cwd=str(ROOT),
                          input=patch_text, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(proc.stdout, end='')
    if proc.returncode != 0:
        raise SystemExit(PREFIX + ' renderer patch failed')
    if MARKER not in R.read_text():
        raise SystemExit(PREFIX + ' renderer marker missing after patch')
    print(PREFIX + ' authoritative publication/lifetime patch applied')
    return True


apply_renderer_patch()
monitor = M.read_text()
monitor, m1 = replace_once(monitor,
    'internal const string Revision = "OH_PHASE6_002";',
    'internal const string Revision = "OH_PHASE6_003";', 'revision identity')
if 'internal const string Codename = "NOREPINEPHRINE";' not in monitor or \
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' not in monitor:
    raise SystemExit(PREFIX + ' NOREPINEPHRINE candidate identity was not inherited')
if m1:
    M.write_text(monitor)

config = C.read_text()
if 'codename = NOREPINEPHRINE' not in config:
    if 'codename = ATROPINE' in config:
        config = config.replace('codename = ATROPINE', 'codename = NOREPINEPHRINE', 1)
    elif 'codename = ADENOSINE' in config:
        config = config.replace('codename = ADENOSINE', 'codename = NOREPINEPHRINE', 1)
    else:
        raise SystemExit(PREFIX + ' Operation Health config codename mismatch')
    C.write_text(config)

build = U.read_text()
build, b1 = replace_once(build,
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT"',
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION"',
    'build display')
build, b2 = replace_once(build,
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV002 STAGED COMMIT',
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION',
    'build checkpoint')
build, b3 = replace_once(build,
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_staged_main_thread_commit_hotfix.py"',
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py"',
    'active Phase6_003 verifier')
build, b4 = replace_once(build,
    "updated, count = re.subn(r'(?m)^(\\s*codename\\s*=\\s*).+$', r'\\1ATROPINE', text, count=1)",
    "updated, count = re.subn(r'(?m)^(\\s*codename\\s*=\\s*).+$', r'\\1NOREPINEPHRINE', text, count=1)",
    'installed OH codename promotion')
build, b5 = replace_once(build,
    '[AERIS] ERROR: Operation Health codename key missing during Phase 4 install promotion',
    '[AERIS] ERROR: Operation Health codename key missing during Phase 6 install promotion',
    'installed OH codename error label')
if any((b1, b2, b3, b4, b5)):
    U.write_text(build)

p5v = P5V.read_text()
old_rev = """phase6_identity = ('internal const string Codename = \"NOREPINEPHRINE\";' in M and
    (('internal const string Revision = \"OH_PHASE6_001\";' in M) or
     ('internal const string Revision = \"OH_PHASE6_002\";' in M)) and"""
new_rev = """phase6_identity = ('internal const string Codename = \"NOREPINEPHRINE\";' in M and
    (('internal const string Revision = \"OH_PHASE6_001\";' in M) or
     ('internal const string Revision = \"OH_PHASE6_002\";' in M) or
     ('internal const string Revision = \"OH_PHASE6_003\";' in M)) and"""
p5v, p1 = replace_once(p5v, old_rev, new_rev, 'Phase5 descendant revision')
old_build = """     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV002 STAGED COMMIT' in U)))"""
new_build = """     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV002 STAGED COMMIT' in U) or
     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION' in U)))"""
p5v, p2 = replace_once(p5v, old_build, new_build, 'Phase5 descendant build')
old_active = """    ('verify_aeris25_main_thread_commit_governor.py' in active) or
    ('verify_aeris25_staged_main_thread_commit_hotfix.py' in active)) and"""
new_active = """    ('verify_aeris25_main_thread_commit_governor.py' in active) or
    ('verify_aeris25_staged_main_thread_commit_hotfix.py' in active) or
    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active)) and"""
p5v, p3 = replace_once(p5v, old_active, new_active, 'Phase5 descendant verifier')
if any((p1, p2, p3)):
    P5V.write_text(p5v)

print(PREFIX + ' AUTHORITATIVE PUBLICATION + DEFERRED RETIREMENT APPLIED')
print('Rule: hidden frames may prepare/upload only; Finalize/publication is authoritative-content-tick-only')
print('Lifetime: replaced Entries detach immediately but Mesh recycle waits until presentationEntryPins releases them')
print('Granularity: packed/contour/coast Mesh mutations are split into acquire/vertex/colour/index/upload stages')
