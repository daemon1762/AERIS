#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OH = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
CACHE = ROOT / 'Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs'
RENDERER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
BUILD = ROOT / 'build_ubuntu.sh'

checks = []

def check(ok, label):
    checks.append((bool(ok), label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)

for p in (OH, CACHE, RENDERER, BUILD):
    check(p.is_file(), 'exists ' + str(p.relative_to(ROOT)))

if not all(p.is_file() for p in (OH, CACHE, RENDERER, BUILD)):
    raise SystemExit(1)

oh = OH.read_text()
cache = CACHE.read_text()
renderer = RENDERER.read_text()
build = BUILD.read_text()

check('internal const string Codename = "NOREPINEPHRINE";' in oh,
      'REV003 codename preserved')
check('internal const string Revision = "OH_PHASE6_003";' in oh,
      'REV003 revision preserved')
check('internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in oh,
      'REV003 candidate preserved')
check('internal const string ObserverVariant = "AERIS26_REV003_OBSERVER_M1";' in oh,
      'observer variant identity present')
check('measurement_only=1' in oh and 'control_delta=0' in oh,
      'measurement-only startup contract present')

for marker in (
    'obs_access=', 'obs_prep_hit=', 'obs_decode_submit=', 'obs_get_hit=',
    'obs_get_miss=', 'obs_ram_commit=', 'obs_decode_fail=', 'obs_evict=',
    'obs_budget_evict=', 'obs_evict_mib=', 'obs_reuse_samples=',
    'obs_reuse_mean_ms=', 'obs_reuse_max_ms=', 'obs_reuse_hist=',
    'obs_rereq_samples=', 'obs_rereq_mean_ms=', 'obs_rereq_max_ms=',
    'obs_rereq_hist=', 'obs_decode_samples=', 'obs_decode_mean_ms=',
    'obs_decode_max_ms=', 'obs_decode_hist=', 'obs_reslife_samples=',
    'obs_reslife_mean_s=', 'obs_reslife_max_s=', 'obs_reslife_hist=',
    'obs_evict_idle_samples=', 'obs_evict_idle_mean_s=',
    'obs_evict_idle_max_s=', 'obs_evict_idle_hist=',
    'obs_lod_evt_g_f_r_l_land=', 'obs_maps=', 'obs_scope_reset=',
    'obs_map_reset=', 'obs_self_mean_us=', 'obs_self_max_us=',
):
    check(marker in oh, 'telemetry ' + marker)

check('using AERISFlightControl.Performance;' in cache,
      'resident cache observer namespace hook')
check(cache.count('RecordRev003ObserverAccess(') == 4,
      'resident access hooks exact count=4')
check(cache.count('RecordRev003ObserverRamCommit(') == 1,
      'RAM commit hook exact count=1')
check(cache.count('RecordRev003ObserverDecodeFailure(') == 1,
      'decode failure hook exact count=1')
check(cache.count('RecordRev003ObserverEviction(') == 1,
      'eviction hook exact count=1')
check(cache.count('RecordRev003ObserverScopeReset(') == 2,
      'scope reset hooks exact count=2')

check('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in renderer,
      'REV003 authoritative publication marker retained')
for forbidden in (
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
    'WaitManagedPreparation',
):
    check(forbidden not in renderer, 'forbidden runtime marker absent: ' + forbidden)

check('OBSERVER_VARIANT="AERIS26_REV003_OBSERVER_M1"' in build,
      'build observer identity present')
check('verify_aeris26_rev003_observer.py' in build,
      'build invokes observer verifier')
check('observer_variant=%s' in build,
      'candidate identity records observer variant')
check('Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs' in build and
      'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs' in build,
      'source tree hash covers observer files')

combined_new = oh + '\n' + cache
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'SubmitRequired(',
                  'ManagedPreparationMaximumInFlight', 'ResidentPreparedPresentation'):
    check(forbidden not in combined_new,
          'observer adds no worker/presentation mechanism: ' + forbidden)

# Cheap structural sanity for the two edited C# files. Strings/comments are ignored.
def brace_sane(text):
    depth = 0
    minimum = 0
    state = 'code'
    i = 0
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'str'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}': depth -= 1; minimum = min(minimum, depth)
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state in ('str', 'char'):
            if c == '\\': i += 2; continue
            if (state == 'str' and c == '"') or (state == 'char' and c == "'"):
                state = 'code'
            i += 1
    return depth == 0 and minimum == 0

check(brace_sane(oh), 'OH C# brace sanity')
check(brace_sane(cache), 'resident cache C# brace sanity')

failed = [label for ok, label in checks if not ok]
if failed:
    print('[AERIS26 REV003 OBSERVER M1] STATIC FAIL count=%d' % len(failed))
    raise SystemExit(1)
print('[AERIS26 REV003 OBSERVER M1] STATIC PASS %d/%d' %
      (len(checks), len(checks)))
