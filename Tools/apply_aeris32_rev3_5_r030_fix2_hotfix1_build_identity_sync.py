#!/usr/bin/env python3
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'

if not VERSION.is_file():
    raise SystemExit('R030 Fix2 Hotfix1 version source missing: ' + str(VERSION))

text = VERSION.read_text()
if MARKER not in text:
    raise SystemExit('R030 Fix2 Hotfix1 requires materialized Fix2 identity')

head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=str(ROOT), text=True).strip()
old_match = re.search(r'internal const string SourceGitSha = "([0-9a-fA-F]*)";', text)
if old_match is None:
    raise SystemExit('R030 Fix2 Hotfix1 SourceGitSha field missing')
old_head = old_match.group(1)
text = re.sub(
    r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
    'internal const string SourceGitSha = "' + head + '";',
    text,
    count=1)
VERSION.write_text(text)

verify = VERSION.read_text()
expected = 'internal const string SourceGitSha = "' + head + '";'
if expected not in verify:
    raise SystemExit('R030 Fix2 Hotfix1 failed to synchronize SourceGitSha')

print('PASS: R030 Fix2 Hotfix1 build identity synchronized')
print('runtime_change=NONE_BUILD_IDENTITY_ONLY')
print('previous_source_git_sha=' + old_head)
print('source_git_sha=' + head)
