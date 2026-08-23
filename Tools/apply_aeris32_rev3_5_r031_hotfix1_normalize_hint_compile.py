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
old = "return value.Trim().Trim('\\\"', '\\\\'').Replace('\\\\\\\\', '/').TrimStart('/').ToLowerInvariant();"
new = "return value.Trim().Trim('\\\"').Replace('\\\\\\\\', '/').TrimStart('/').ToLowerInvariant();"
if old in text:
    text = text.replace(old, new, 1)
elif new not in text:
    # Accept the exact C# rendering produced by Python raw strings as a second anchor.
    old2 = "return value.Trim().Trim('\\\"', '\\\'').Replace('\\\\\\\\', '/').TrimStart('/').ToLowerInvariant();"
    if old2 in text:
        text = text.replace(old2, new, 1)
    else:
        raise SystemExit('R031 Hotfix1 NormalizeHint anchor not found')
SOURCE.write_text(text)
print('PASS: R031 Hotfix1 NormalizeHint compile fix materialized')
print('runtime_behavior_change=NONE equivalent quote-trim simplification')
