#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 RESOURCE RELEASE ORDER HOTFIX2 VERIFY]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
HF2 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
    depth = 0; state = 'code'; i = op
    while i < len(text):
        c = text[i]; n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return text[start:i + 1]
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


check(R.is_file(), 'renderer exists')
check(B.is_file(), 'build exists')
if not R.is_file() or not B.is_file(): raise SystemExit(1)
r = R.read_text(); b = B.read_text()
for marker in (R006, HF1, HF2):
    check(marker in r, 'marker retained: ' + marker)
reset = method(r, '        void ResetContentSnapshot()')
release = method(r, '        void ReleaseGpuResources()')
check(reset, 'ResetContentSnapshot resolved')
check(release, 'ReleaseGpuResources resolved')
check('CancelPendingEntryCommit();' in reset,
      'reset can recycle active pending commit and therefore requires post-reset drain')
check('ReleaseDeferredEntryRetirements(true);' in reset,
      'reset can release deferred snapshot Entries and therefore requires post-reset drain')
reset_pos = release.find('ResetContentSnapshot();')
last_mesh = release.rfind('DestroyMeshPool();')
last_geo = release.rfind('ClearRev35R006GeographicPool();')
rt_pos = release.find('DestroyRenderTargets();')
check(0 <= reset_pos < last_mesh < last_geo < rt_pos,
      'final native/managed pool drain occurs after reset and before render-target destruction')
check(release.count('DestroyMeshPool();') == 2,
      'full release has pre-reset and final post-reset native pool drains')
check(release.count('ClearRev35R006GeographicPool();') == 2,
      'full release has pre-reset and final post-reset managed pool drains')
check('REV3_5_R006_HOTFIX2="' + HF2 + '"' in b,
      'build HF2 identity present')
check('verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py' in b,
      'build invokes HF2 verifier')
check('rev3_5_r006_hotfix2=%s' in b,
      'candidate identity records HF2')
check('presentationNow + 0.10f' in r,
      'fixed 10 Hz authority unchanged')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    check(forbidden not in r, 'rejected mechanism absent: ' + forbidden)
failed = [label for ok, label in checks if not ok]
if failed:
    print(PREFIX + ' FAIL: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
