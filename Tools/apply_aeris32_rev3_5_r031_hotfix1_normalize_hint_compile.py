#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Source/AERISFlightControl/Terrain/AERISPtcSourceResolver.cs'
MARKER = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'

if not SOURCE.is_file():
    raise SystemExit('R031 source resolver missing; run R031 applicator first')
text = SOURCE.read_text()
if MARKER not in text:
    raise SystemExit('R031 source resolver marker missing')
lines = text.splitlines(True)
changed = False
for i, line in enumerate(lines):
    if 'return value.Trim().Trim(' in line and 'ToLowerInvariant();' in line:
        indent = line[:len(line) - len(line.lstrip())]
        lines[i] = indent + "return value.Trim().Trim('\\\"').Replace('\\\\', '/').TrimStart('/').ToLowerInvariant();\n"
        changed = True
        break
if not changed:
    raise SystemExit('R031 Hotfix1 NormalizeHint return line not found')
SOURCE.write_text(''.join(lines))
print('PASS: R031 Hotfix1 NormalizeHint compile fix materialized')
print('runtime_behavior_change=NONE equivalent quote-trim simplification')
