#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, sha256

suite = CheckSuite("v0.17.0.2 internal manifest verification")
manifest = ROOT / "MANIFEST_SHA256.txt"
suite.check(manifest.is_file(), "MANIFEST_SHA256.txt exists")
expected = {}
if manifest.is_file():
    for number, line in enumerate(manifest.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip(): continue
        parts = line.split("  ", 1)
        if len(parts) != 2 or len(parts[0]) != 64:
            suite.check(False, "manifest line " + str(number) + " format", line)
            continue
        expected[parts[1]] = parts[0].lower()
actual = sorted(str(path.relative_to(ROOT)).replace('\\', '/') for path in ROOT.rglob('*')
                if path.is_file() and path.name != "MANIFEST_SHA256.txt" and
                "__pycache__" not in path.parts and path.suffix.lower() != ".pyc")
suite.equal(sorted(expected), actual, "manifest file set matches package")
bad = [name for name in actual if expected.get(name) != sha256(ROOT / name)]
suite.check(not bad, "all manifest hashes match", ", ".join(bad[:10]))
suite.finish()
