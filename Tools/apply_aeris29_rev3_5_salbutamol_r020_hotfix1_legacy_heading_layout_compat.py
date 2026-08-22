#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX = '[AERIS30 REV3.5 R020 HOTFIX1 LEGACY HEADING LAYOUT COMPAT]'
R020 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY'
SIGNATURE = '        void UpdateDisplayView(double latitudeDeg, double longitudeDeg,'
CANONICAL = 'Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading)) > 3.0'


def fail(message):
    raise SystemExit(PREFIX + ' FAIL ' + message)


def method_bounds(text, signature):
    starts = []
    pos = 0
    while True:
        pos = text.find(signature, pos)
        if pos < 0:
            break
        starts.append(pos)
        pos += len(signature)
    if len(starts) != 1:
        fail('UpdateDisplayView method count=%d' % len(starts))

    start = starts[0]
    op = text.find('{', start)
    if op < 0:
        fail('UpdateDisplayView opening brace missing')

    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'
                i += 2
                continue
            if c == '/' and n == '*':
                state = 'block'
                i += 2
                continue
            if c == '"':
                state = 'string'
                i += 1
                continue
            if c == "'":
                state = 'char'
                i += 1
                continue
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return start, i + 1
            i += 1
            continue
        if state == 'line':
            if c == '\n':
                state = 'code'
            i += 1
            continue
        if state == 'block':
            if c == '*' and n == '/':
                state = 'code'
                i += 2
                continue
            i += 1
            continue
        if state == 'string':
            if c == '\\':
                i += 2
                continue
            if c == '"':
                state = 'code'
            i += 1
            continue
        if state == 'char':
            if c == '\\':
                i += 2
                continue
            if c == "'":
                state = 'code'
            i += 1
            continue

    fail('UpdateDisplayView closing brace missing')


if not T.is_file():
    fail('missing ' + str(T.relative_to(ROOT)))

text = T.read_text(encoding='utf-8')

# The wrapper must remain idempotent after the base R020 overlay has materialized.
# In that state the legacy displayViewHeadingDeg authority expression is intentionally
# gone, so there is nothing left for this compatibility normalizer to touch.
if R020 in text:
    print(PREFIX + ' R020 already materialized; compatibility normalization not required')
    print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
    raise SystemExit(0)

m0, m1 = method_bounds(text, SIGNATURE)
method = text[m0:m1]

# Match the complete legacy headingChanged semantic statement, not an arbitrary
# DeltaAngle occurrence elsewhere in the file. Whitespace/newline placement may vary,
# but every authority operand and the strict > 3.0 threshold must remain unchanged.
pattern = re.compile(
    r'(?P<indent>^[ \t]*)bool\s+headingChanged\s*=\s*!displayViewValid\s*\|\|\s*'
    r'\(\s*trackUp\s*&&\s*'
    r'(?P<expr>Math\s*\.\s*Abs\s*\(\s*DeltaAngle\s*\(\s*'
    r'displayViewHeadingDeg\s*,\s*normalizedHeading\s*\)\s*\)\s*>\s*3\.0)'
    r'\s*\)\s*;',
    re.MULTILINE,
)
matches = list(pattern.finditer(method))
if len(matches) != 1:
    fail('semantic heading threshold matches=%d' % len(matches))

match = matches[0]
expr = match.group('expr')
if expr == CANONICAL:
    print(PREFIX + ' already canonical')
    print('semantic_expression=' + CANONICAL)
    print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
    raise SystemExit(0)

# Normalize only the formatting of the proven legacy threshold expression. The
# surrounding headingChanged logic, Track-Up guard, operands and 3.0-degree policy
# remain byte-for-byte untouched outside this expression.
expr0 = m0 + match.start('expr')
expr1 = m0 + match.end('expr')
text = text[:expr0] + CANONICAL + text[expr1:]

# Fail closed before writing if the resulting method no longer contains exactly the
# canonical semantic contract required by the base R020 applicator.
new_m0, new_m1 = method_bounds(text, SIGNATURE)
new_method = text[new_m0:new_m1]
if new_method.count(CANONICAL) != 1:
    fail('post-normalization canonical count=%d' % new_method.count(CANONICAL))

T.write_text(text, encoding='utf-8')

print(PREFIX + ' APPLY PASS')
print('scope=UpdateDisplayView legacy heading whitespace/layout normalization only before R020 applicator')
print('semantic_expression=' + CANONICAL)
print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
