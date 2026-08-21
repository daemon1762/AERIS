#!/usr/bin/env python3
from pathlib import Path
import argparse
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS29 REV3.5 R020 HOTFIX1 FAST BUILD]'
BRANCH = 'agent/aeris29-rev3-5-salbutamol-r020-current-revision-publication-catch-up'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()


parser = argparse.ArgumentParser(
    description='R020 FAST Hotfix1 wrapper: normalize legacy heading layout, then run base R020 FAST build.')
parser.add_argument('ksp_path')
args = parser.parse_args()

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r020_hotfix1_legacy_heading_layout_compat.py'])
run([sys.executable,
     ROOT / 'Tools/build_aeris29_rev3_5_salbutamol_r020_fast.py',
     str(Path(args.ksp_path).expanduser().resolve())])

print(PREFIX + ' PASS')
print('scope=tooling compatibility only before base R020 applicator')
print('runtime_change=R020 base only; Hotfix1 adds no runtime policy change')
