#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
MARKER = 'AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW'

if not VERSION.is_file():
    raise SystemExit('R031 generated build version missing')
text = VERSION.read_text()
if MARKER not in text:
    raise SystemExit('R031 runtime source graph identity missing before sync')
head = subprocess.check_output(['git','rev-parse','HEAD'], cwd=str(ROOT), text=True).strip()
h = hashlib.sha256()
files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs')) + [CSPROJ]
for path in files:
    if path == VERSION: continue
    h.update(str(path.relative_to(ROOT)).encode()); h.update(b'\0')
    h.update(path.read_bytes()); h.update(b'\0')
tree = h.hexdigest()
text = re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
    'internal const string SourceGitSha = "' + head + '";', text)
text = re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
    'internal const string SourceTreeSha256 = "' + tree + '";', text)
VERSION.write_text(text)
print('PASS: R031 runtime source graph build identity synchronized')
print('source_git_sha=' + head)
print('source_tree_sha256=' + tree)
