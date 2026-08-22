#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX = '[AERIS30 REV3.5 R020 HOTFIX2 HEADING SUCCESSOR COMPAT]'
R020 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY'
SIGNATURE = '        void UpdateDisplayView(double latitudeDeg, double longitudeDeg,'
CANONICAL_3 = 'Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading)) > 3.0'


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
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return start, i + 1
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue

    fail('UpdateDisplayView closing brace missing')


if not T.is_file():
    fail('missing ' + str(T.relative_to(ROOT)))

text = T.read_text(encoding='utf-8')

if R020 in text:
    print(PREFIX + ' R020 already materialized; compatibility normalization not required')
    print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
    raise SystemExit(0)

m0, m1 = method_bounds(text, SIGNATURE)
method = text[m0:m1]

# Successor tree support: AERIS25 deliberately changed hidden Track-Up planning from
# the historical strict >3deg expression to a cumulative >=6deg burst governor. That
# is an intentional runtime policy and MUST NOT be normalized back to 3deg by tooling.
burst_tokens = (
    'AERIS25_CONTENT_GENERATION_BURST_GOVERNOR',
    'double planningHeadingDelta = !displayViewValid ? double.MaxValue :',
    'Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading));',
    'planningHeadingDelta >= 6.0',
    'bool structuralViewChanged = rangeChanged || centerChanged ||',
    'bool acceptPlanningHeading = !displayViewValid || !trackUp ||',
    'if (acceptPlanningHeading) displayViewHeadingDeg = normalizedHeading;',
)
burst6 = all(token in method for token in burst_tokens)

# Historical R020 input remains supported. Only whitespace/layout in the proven strict
# >3deg expression may be canonicalized; no threshold or operand is changed.
pattern3 = re.compile(
    r'(?P<indent>^[ \t]*)bool\s+headingChanged\s*=\s*!displayViewValid\s*\|\|\s*'
    r'\(\s*trackUp\s*&&\s*'
    r'(?P<expr>Math\s*\.\s*Abs\s*\(\s*DeltaAngle\s*\(\s*'
    r'displayViewHeadingDeg\s*,\s*normalizedHeading\s*\)\s*\)\s*>\s*3\.0)'
    r'\s*\)\s*;',
    re.MULTILINE,
)
matches3 = list(pattern3.finditer(method))
legacy3 = len(matches3) == 1

if burst6 and legacy3:
    fail('ambiguous heading policy: both legacy3 and successor6 detected')
if burst6:
    print(PREFIX + ' successor AERIS25 cumulative >=6deg burst governor detected')
    print('heading_policy=preserve_successor_cumulative_ge_6deg')
    print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
    raise SystemExit(0)
if not legacy3:
    fail('unsupported heading policy: legacy3 matches=%d successor6=%s' %
         (len(matches3), 'yes' if burst6 else 'no'))

match = matches3[0]
expr = match.group('expr')
if expr == CANONICAL_3:
    print(PREFIX + ' historical strict >3deg input already canonical')
    print('heading_policy=preserve_legacy_strict_gt_3deg')
    print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
    raise SystemExit(0)

expr0 = m0 + match.start('expr')
expr1 = m0 + match.end('expr')
text = text[:expr0] + CANONICAL_3 + text[expr1:]
new_m0, new_m1 = method_bounds(text, SIGNATURE)
new_method = text[new_m0:new_m1]
if new_method.count(CANONICAL_3) != 1:
    fail('post-normalization canonical count=%d' % new_method.count(CANONICAL_3))

T.write_text(text, encoding='utf-8')
print(PREFIX + ' APPLY PASS')
print('scope=historical UpdateDisplayView whitespace/layout normalization only')
print('heading_policy=preserve_legacy_strict_gt_3deg')
print('semantic_expression=' + CANONICAL_3)
print('runtime_policy_change=0 threshold_change=0 publication_change=0 worker_change=0')
