#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 RESOURCE RELEASE HOTFIX1 VERIFY]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
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
    depth = 0
    i = op
    while i < len(text):
        if text[i] == '{': depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0: return text[start:i + 1]
        i += 1
    return ''


check(R.is_file(), 'renderer exists')
check(B.is_file(), 'build exists')
if not R.is_file() or not B.is_file(): raise SystemExit(1)
r = R.read_text(); b = B.read_text()
check(R006 in r, 'R006 parent retained')
check(HF1 in r, 'HF1 marker present')
release = method(r, '        void ReleaseGpuResources()')
check(release, 'ReleaseGpuResources resolved')
check('DestroyMeshPool();' in release and
      'ClearRev35R006GeographicPool();' in release and
      'identityIndexCache.Clear();' in release,
      'full GPU/resource release clears native and R006 managed pools')
check(release.find('DestroyMeshPool();') <
      release.find('ClearRev35R006GeographicPool();') <
      release.find('identityIndexCache.Clear();'),
      'R006 pool clear is inside existing full-release boundary')
recycle = method(r,
    '        void RecycleRev35R006GeographicBuffer(ref GeographicUnitPoint[] buffer)')
check(recycle and 'stack.Push(buffer)' in recycle,
      'ordinary Entry retirement still retains bounded reuse')
check(r.count('ClearRev35R006GeographicPool();') == 1,
      'managed pool is cleared only by full resource release')
check('REV3_5_R006_HOTFIX1="' + HF1 + '"' in b,
      'build HF1 identity present')
check('verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py' in b,
      'build invokes HF1 verifier')
check('rev3_5_r006_hotfix1=%s' in b,
      'candidate identity records HF1')
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
