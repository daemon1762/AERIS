#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R030 FIX2 HF1 BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r030-preload-persistence-ptc-phase0'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()

if len(sys.argv) != 2:
    raise SystemExit('usage: build_aeris32_rev3_5_r030_fix2_hotfix1_simple.py <KSP path>')

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

# Reuse the accepted Fix2 build pipeline, but synchronize the generated runtime
# SourceGitSha to the actual current branch HEAD immediately before compilation.
run([sys.executable,
    ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix2_hotfix1_build_identity_sync.py'])
run([sys.executable,
    ROOT / 'Tools/build_aeris32_rev3_5_r030_fix2_simple.py', sys.argv[1]])

head = output(['git', 'rev-parse', 'HEAD'])
version = (ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs').read_text()
expected = 'internal const string SourceGitSha = "' + head + '";'
if expected not in version:
    raise SystemExit(PREFIX + ' generated runtime SourceGitSha does not match HEAD')

print(PREFIX + ' PASS')
print('runtime_change=NONE_BUILD_IDENTITY_HOTFIX_ONLY')
print('source_git_sha=' + head)
print('NOTE: fully exit and restart KSP after DLL replacement; one Main Menu startup is sufficient for log acceptance.')
