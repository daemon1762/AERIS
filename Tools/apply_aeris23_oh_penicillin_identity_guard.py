#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

base = ROOT / "Tools/apply_aeris23_staggered_exact_refresh_identity_guard.py"
if not base.is_file():
    raise SystemExit("[PENICILLIN IDENTITY] stagger identity guard missing")

subprocess.run([sys.executable, str(base)], cwd=str(ROOT), check=True)

build = ROOT / "build_ubuntu.sh"
text = build.read_text()

old = 'CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
new = 'CANDIDATE_NAME="AERIS23_OH_PENICILLIN"'
if old in text:
    text = text.replace(old, new, 1)
elif new not in text:
    raise SystemExit("[PENICILLIN IDENTITY] candidate-name anchor missing")

old_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
new_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_oh_penicillin_source.py"'
if old_verify in text:
    text = text.replace(old_verify, new_verify, 1)
elif new_verify not in text:
    raise SystemExit("[PENICILLIN IDENTITY] verifier anchor missing")

build.write_text(text)
verified = build.read_text()

if new not in verified or old in verified:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: stale Stagger candidate identity survived")
if new_verify not in verified or old_verify in verified:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: build preflight still targets Stagger verifier")
if "AERISOperationHealth.cfg" not in verified:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: Operation Health config is not preserved by installer")

print("[PENICILLIN IDENTITY] candidate=AERIS23_OH_PENICILLIN")
print("[PENICILLIN IDENTITY] build preflight now requires PENICILLIN source verifier")
print("[PENICILLIN IDENTITY] stale Stagger identity check PASS")
