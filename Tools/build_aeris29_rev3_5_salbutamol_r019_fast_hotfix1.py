#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS29 REV3.5 R019 HOTFIX1 FAST BUILD]'
BRANCH = 'agent/aeris29-rev3-5-salbutamol-r019-visible-far-commit-priority'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
HF1 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode() in data or text.encode('utf-16le') in data


parser = argparse.ArgumentParser(description='R019 Hotfix1 FAST build wrapper')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch)

# Materialize base R019 first, then apply the narrow wake/backlog + historical-test hotfix.
run([sys.executable, ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py'])
run([sys.executable, ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_hotfix1_wake_backlog_integration.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris29_rev3_5_salbutamol_r019_hotfix1_wake_backlog_integration.py'])

# Reuse the normal R019 FAST gate/build/install. Its repeated applicators are idempotent.
run([sys.executable, ROOT / 'Tools/build_aeris29_rev3_5_salbutamol_r019_fast.py', str(ksp)])

repo_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed = ksp / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
identity = ROOT / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
installed_identity = ksp / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
for path in (repo_dll, installed, identity, installed_identity):
    if not path.is_file():
        raise SystemExit(PREFIX + ' missing post-build artifact: ' + str(path))

# Add development-hotfix identity only after the base FAST build succeeded.
hf_line = 'rev3_5_r019_hotfix1=' + HF1 + '\n'
ident = identity.read_text()
if ('rev3_5_r019_variant=' + R019) not in ident:
    raise SystemExit(PREFIX + ' R019 parent identity missing')
if hf_line not in ident:
    if ident and not ident.endswith('\n'):
        ident += '\n'
    ident += hf_line
    identity.write_text(ident)
shutil.copy2(str(identity), str(installed_identity))

repo_sha = sha256(repo_dll)
installed_sha = sha256(installed)
dll = installed.read_bytes()
checks = (
    (repo_sha == installed_sha, 'repo/installed DLL SHA match'),
    (marker_in_bytes(dll, R019), 'DLL embeds R019 parent marker'),
    (marker_in_bytes(dll, HF1), 'DLL embeds R019 Hotfix1 marker'),
    (marker_in_bytes(dll, 'oh_rev35_r019_hf1_variant='), 'DLL embeds Hotfix1 telemetry identity'),
    (marker_in_bytes(dll, 'rev35R019VisibleFoundationQueue'), 'DLL embeds visible priority queue'),
    (hf_line.strip() in installed_identity.read_text(), 'installed identity records Hotfix1'),
)
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

print(PREFIX + ' PASS')
print('mode=FAST development Hotfix1')
print('formal_replay=NO full_prebuild=NO bin_obj_clean=NO')
print('runtime_change=wake/backlog integration only; queue cap 128, single commit lane and R004 2.00ms max retained')
print('dll_sha256=' + installed_sha)
print('NOTE: restart KSP after DLL replacement.')
