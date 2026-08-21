#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX = '[AERIS29 REV3.5 R020 HOTFIX1 LEGACY HEADING LAYOUT COMPAT]'

if not T.is_file():
    raise SystemExit(PREFIX + ' FAIL missing ' + str(T.relative_to(ROOT)))

text = T.read_text()
canonical = 'Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading)) > 3.0'

if canonical in text:
    print(PREFIX + ' already canonical')
    raise SystemExit(0)

pattern = re.compile(
    r'Math\.Abs\(DeltaAngle\(displayViewHeadingDeg,\s*normalizedHeading\)\)\s*>\s*3\.0')
matches = list(pattern.finditer(text))
if len(matches) != 1:
    raise SystemExit(PREFIX + ' FAIL semantic heading threshold matches=%d' % len(matches))

old = matches[0].group(0)
if 'displayViewHeadingDeg' not in old or 'normalizedHeading' not in old or '3.0' not in old:
    raise SystemExit(PREFIX + ' FAIL semantic guard mismatch')

text = text[:matches[0].start()] + canonical + text[matches[0].end():]
T.write_text(text)

print(PREFIX + ' APPLY PASS')
print('scope=whitespace/layout normalization only before R020 applicator')
print('semantic_expression=' + canonical)
print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
