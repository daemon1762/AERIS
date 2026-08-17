#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
PREFIX = '[AERIS27 R006 GENERATED TREE AUDIT]'

if not R.is_file():
    raise SystemExit(PREFIX + ' renderer missing')
text = R.read_text()
required = (
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES',
    'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION',
    'AERIS26_REV003_OBSERVER_M1',
)
for marker in required:
    if marker not in text:
        raise SystemExit(PREFIX + ' missing required generated marker: ' + marker)


def method(signature):
    start = text.find(signature)
    if start < 0:
        print(PREFIX + ' METHOD MISSING ' + signature)
        return ''
    op = text.find('{', start)
    if op < 0:
        print(PREFIX + ' METHOD OPEN MISSING ' + signature)
        return ''
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
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return text[start:i + 1]
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
    return ''

signatures = (
    '        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,',
    '        bool AdvancePendingGeographic(Vector3[] source,',
    '        bool FinalizePendingEntryCommit(PendingEntryCommit pending,',
    '        void CancelPendingEntryCommit()',
    '        void ReleaseDeferredEntryRetirements(bool force)',
    '        void Remove(Entry entry)',
    '        bool UploadGpuGeographicAttribute(Mesh mesh, GeographicUnitPoint[] points)',
    '        void SwapFrontAndBack(AERISTerrainVisibleTileSet visible, Vessel vessel,',
)

for signature in signatures:
    body = method(signature)
    print('\n===== ' + signature.strip() + ' =====')
    if not body:
        continue
    for line in body.splitlines():
        if any(token in line for token in (
            'Geographic', 'Projected', 'PackedSource', 'PackedColours', 'PackedIndices',
            'RecycleMesh', 'presentationEntryPins', 'deferredEntryRetirements',
            'allowPublication', 'Finalize', 'new ', '.Clone(', 'Capacity', 'frontBufferSwaps',
            'frontCommittedRealtime', 'renderReadyFields', 'pendingEntryCommit')):
            print(line)

print('\n===== ALLOCATION SITE COUNTS =====')
patterns = (
    r'new GeographicUnitPoint\[',
    r'new Vector3\[',
    r'new Color32\[',
    r'new int\[',
    r'\.Clone\(\)',
    r'\.ToArray\(\)',
    r'\.Capacity =',
)
for pattern in patterns:
    print(pattern + '=' + str(len(re.findall(pattern, text))))

print('\n===== TARGET FIELDS =====')
for token in (
    'PackedTerrainGeographicPoints', 'ContourGeographicPoints',
    'CoastlineGeographicPoints', 'PackedTerrainProjectedVertices',
    'ContourProjectedVertices', 'CoastlineProjectedVertices',
    'FinalizeReadyTicks', 'deferredEntryRetirements', 'presentationEntryPins',
    'oh_main_commit_publish_defer=', 'oh_prune_debt_peak_bytes=',
):
    print(token + '=' + str(text.count(token)))

print(PREFIX + ' PASS')
# Audit trigger revision 2: workflow now exists on the branch before this push.
