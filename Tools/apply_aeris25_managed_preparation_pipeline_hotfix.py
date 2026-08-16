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
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_004]'
MARKER = 'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE'
PATCH_B64 = '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'

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
        print(PREFIX + ' managed preparation renderer patch already present')
        return
    if 'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' not in renderer or \
       'OH_PHASE6_003' not in M.read_text():
        raise SystemExit(PREFIX + ' generated Phase6_003 parent is required')
    patch_text = zlib.decompress(base64.b64decode(PATCH_B64)).decode('utf-8')
    proc = subprocess.run(['patch','--batch','--forward','-p0'], cwd=str(ROOT),
                          input=patch_text, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(proc.stdout, end='')
    if proc.returncode != 0:
        raise SystemExit(PREFIX + ' canonical renderer patch failed')
    if MARKER not in R.read_text():
        raise SystemExit(PREFIX + ' renderer marker missing after patch')
    print(PREFIX + ' managed preparation renderer patch applied')

apply_renderer_patch()

monitor = M.read_text()
monitor, changed = replace_once(monitor,
    'internal const string Revision = "OH_PHASE6_003";',
    'internal const string Revision = "OH_PHASE6_004";', 'revision identity')
if changed:
    M.write_text(monitor)
if 'internal const string Codename = "NOREPINEPHRINE";' not in monitor or \
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' not in monitor:
    raise SystemExit(PREFIX + ' inherited NOREPINEPHRINE identity mismatch')
if 'codename = NOREPINEPHRINE' not in C.read_text():
    raise SystemExit(PREFIX + ' Operation Health config must already be NOREPINEPHRINE')

build = U.read_text()
build, b1 = replace_once(build,
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION"',
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE"',
    'build display')
build, b2 = replace_once(build,
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION',
    'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE',
    'build checkpoint')
build, b3 = replace_once(build,
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py"',
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_managed_preparation_pipeline_hotfix.py"',
    'active rev004 verifier')
if any((b1,b2,b3)):
    U.write_text(build)

p5v = P5V.read_text()
old_rev = (
    'phase6_identity = (\'internal const string Codename = "NOREPINEPHRINE";\' in M and\n'
    '    ((\'internal const string Revision = "OH_PHASE6_001";\' in M) or\n'
    '     (\'internal const string Revision = "OH_PHASE6_002";\' in M) or\n'
    '     (\'internal const string Revision = "OH_PHASE6_003";\' in M)) and')
new_rev = (
    'phase6_identity = (\'internal const string Codename = "NOREPINEPHRINE";\' in M and\n'
    '    ((\'internal const string Revision = "OH_PHASE6_001";\' in M) or\n'
    '     (\'internal const string Revision = "OH_PHASE6_002";\' in M) or\n'
    '     (\'internal const string Revision = "OH_PHASE6_003";\' in M) or\n'
    '     (\'internal const string Revision = "OH_PHASE6_004";\' in M)) and')
p5v, p1 = replace_once(p5v, old_rev, new_rev, 'Phase5 descendant revision')
old_build = (
    "     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in U and\n"
    "      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION' in U)))")
new_build = (
    "     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in U and\n"
    "      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION' in U) or\n"
    "     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in U and\n"
    "      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE' in U)))")
p5v, p2 = replace_once(p5v, old_build, new_build, 'Phase5 descendant build')
old_active = "    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active)) and"
new_active = (
    "    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active) or\n"
    "    ('verify_aeris25_managed_preparation_pipeline_hotfix.py' in active)) and")
p5v, p3 = replace_once(p5v, old_active, new_active, 'Phase5 descendant verifier')
if any((p1,p2,p3)):
    P5V.write_text(p5v)

print(PREFIX + ' MANAGED PREPARATION PIPELINE APPLIED')
print('Design: clip on main -> AERIS GeneralCompute managed prep -> Unity Mesh upload on main -> authoritative-only Finalize')
print('Lifetime: Phase6_003 authoritative publication + deferred retirement retained unchanged')
print('Allocation: GPU normal path skips double geographic/projected fallback arrays and shares immutable render-ready arrays')
