#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

build = ROOT / "build_ubuntu.sh"
text = build.read_text()

old = 'CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
new = 'CANDIDATE_NAME="AERIS23_OH_PENICILLIN"'
if old in text:
    text = text.replace(old, new, 1)
elif new not in text:
    raise SystemExit("[PENICILLIN IDENTITY] expected Stagger candidate identity is missing")

old_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
new_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_oh_penicillin_candidate.py"'
if old_verify in text:
    text = text.replace(old_verify, new_verify, 1)
elif new_verify not in text:
    raise SystemExit("[PENICILLIN IDENTITY] expected Stagger verifier anchor is missing")

build.write_text(text)
verified = build.read_text()

if verified.count(new) != 1 or old in verified:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: executable candidate identity is mixed")
if verified.count(new_verify) != 1 or old_verify in verified:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: build preflight verifier is mixed")
if verified.count("AERISOperationHealth.cfg") < 2:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: Operation Health config is not preserved")

print("[PENICILLIN IDENTITY] candidate=AERIS23_OH_PENICILLIN")
print("[PENICILLIN IDENTITY] build preflight=verify_aeris23_oh_penicillin_candidate.py")
print("[PENICILLIN IDENTITY] single-step Stagger -> PENICILLIN promotion PASS")
