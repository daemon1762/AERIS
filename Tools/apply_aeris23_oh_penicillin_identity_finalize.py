#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]


def active_count(text, line):
    target = line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip() == target)


def replace_active_line(text, old, new, label):
    old_count = active_count(text, old)
    new_count = active_count(text, new)
    if old_count == 1 and new_count == 0:
        lines = text.splitlines(True)
        for i, raw in enumerate(lines):
            if raw.strip() == old.strip():
                ending = "\n" if raw.endswith("\n") else ""
                indent = raw[:len(raw) - len(raw.lstrip())]
                lines[i] = indent + new.strip() + ending
                return "".join(lines)
        raise SystemExit("[PENICILLIN IDENTITY] " + label + ": active line disappeared")
    if old_count == 0 and new_count == 1:
        return text
    raise SystemExit("[PENICILLIN IDENTITY] " + label +
                     ": expected one active old or new line; old=%d new=%d" %
                     (old_count, new_count))

build = ROOT / "build_ubuntu.sh"
text = build.read_text()

old = 'CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
new = 'CANDIDATE_NAME="AERIS23_OH_PENICILLIN"'
text = replace_active_line(text, old, new, "candidate-name promotion")

old_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
new_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_oh_penicillin_candidate.py"'
text = replace_active_line(text, old_verify, new_verify, "build verifier promotion")

build.write_text(text)
verified = build.read_text()

if active_count(verified, new) != 1 or active_count(verified, old) != 0:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: executable candidate identity is mixed")
if active_count(verified, new_verify) != 1 or active_count(verified, old_verify) != 0:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: executable build preflight verifier is mixed")
if verified.count("AERISOperationHealth.cfg") < 2:
    raise SystemExit("[PENICILLIN IDENTITY] FATAL: Operation Health config is not preserved")

print("[PENICILLIN IDENTITY] candidate=AERIS23_OH_PENICILLIN")
print("[PENICILLIN IDENTITY] build preflight=verify_aeris23_oh_penicillin_candidate.py")
print("[PENICILLIN IDENTITY] active-line Stagger -> PENICILLIN promotion PASS")
