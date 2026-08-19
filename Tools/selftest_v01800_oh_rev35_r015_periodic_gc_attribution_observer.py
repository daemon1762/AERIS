#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
O = ROOT / 'Source/AERISFlightControl/Performance/AERISR015PeriodicGcAttributionObserver.cs'
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
P = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[OH REV3.5 R015 PERIODIC GC ATTRIBUTION OBSERVER]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R015 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER'


def block_bounds(text, op):
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return op, i + 1
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
    return -1, -1


def method_body(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
    _, end = block_bounds(text, op)
    return text[start:end] if end > op else ''


for path in (O, R, P, B, PRE):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

observer = O.read_text()
renderer = R.read_text()
project = P.read_text()
build = B.read_text()
prebuild = PRE.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R014 in renderer, 'formal R014 renderer parent retained')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check(R015 in observer, 'R015 observer identity present')
check('[OH_REV3_5_R015_GC_ATTR]' in observer, 'dedicated R015 GC telemetry prefix present')
check('[KSPAddon(KSPAddon.Startup.MainMenu, true)]' in observer,
      'observer is one persistent KSP addon')
check('const float SampleIntervalSeconds = 0.10f;' in observer,
      'GC/heap sample cadence is 10 Hz')
check('const float AttributionWindowSeconds = 5.0f;' in observer,
      'attribution window is five seconds')
check('const int RingCapacity = 64;' in observer,
      'fixed ring has >= five-second capacity at 10 Hz')
check('readonly HeapSample[] heapRing = new HeapSample[RingCapacity];' in observer,
      'heap ring is allocated once and reused')
check('GC.CollectionCount(0)' in observer and 'GC.CollectionCount(1)' in observer and
      'GC.CollectionCount(2)' in observer,
      'all managed GC generation counters are sampled')
check('GC.GetTotalMemory(false)' in observer,
      'managed heap is observed without forcing a collection')
check('if (delta2 > 0)' in observer and 'EmitFullGcEvent(' in observer,
      'Gen2 collection is the event attribution trigger')
check('lastFullGcRealtime' in observer and 'period_s=' in observer,
      'full-GC periodicity is directly measured')
check('heap_start=' in observer and 'heap_peak=' in observer and
      'heap_pre=' in observer and 'heap_post=' in observer and
      'heap_growth=' in observer and 'heap_reclaimed=' in observer,
      'pre/post five-second heap trend is logged')

sample = method_body(observer, '        void SampleFlight(float now)')
event = method_body(observer, '        void EmitFullGcEvent(')
read_counters = method_body(observer, '        static TerrainCounters ReadTerrainCounters(')
resolve_target = method_body(observer, '        void ResolveTarget()')
check(bool(sample) and bool(event) and bool(read_counters) and bool(resolve_target),
      'observer method boundaries resolve')
check('AERISLogger.' not in sample and '.ToString(' not in sample and
      'string.Format' not in sample,
      'normal 10 Hz sample path creates no diagnostic strings/log records')
check('ReadTerrainCounters(' not in sample,
      'normal 10 Hz sample path does not reflect renderer value counters')
check('ReadTerrainCounters(renderer);' in event,
      'renderer cumulative counters are sampled on Full-GC event')
check(observer.count('ReadTerrainCounters(renderer);') == 2,
      'renderer counter reflection is limited to target baseline + Full-GC event')
check('nextTargetRefreshRealtime = now + TargetRefreshSeconds;' in observer,
      'target rebinding is bounded to at most 1 Hz')
check('AERISLogger.Info(LogPrefix +' in event,
      'one compact attribution record is emitted by the event path')
check('terrain_heavy_idle=' in event and
      'delta.ContentTicks == 0' in event and
      'delta.ContentCaptures == 0' in event and
      'delta.ResolveCalls == 0' in event and
      'delta.Publications == 0' in event and
      'delta.FullReconciles == 0' in event,
      'negative attribution requires all heavy terrain activity classes to be idle')

for field in ('operationHealthContentTicks', 'operationHealthContentCaptures',
              'operationHealthResolveCalls', 'operationHealthRev35R014PublicationEvents',
              'operationHealthRev35R014FullReconciles',
              'operationHealthRev35R014WorkerOnlySkips',
              'operationHealthRev35R014PublicationDeferrals',
              'operationHealthRev35R014PublicationReconciles',
              'operationHealthRev35R014RetryReconciles', 'frontBufferSwaps'):
    check(('RendererField("' + field + '")') in observer,
          'event attribution observes R014/terrain counter ' + field)

check('Performance\\AERISR015PeriodicGcAttributionObserver.cs' in project,
      'R015 observer is compiled')
check('REV3_5_R015_VARIANT="' + R015 + '"' in build,
      'R015 build identity variable present')
check('rev3_5_r015_variant=%s' in build,
      'R015 candidate identity emission present')
check('verify_aeris29_rev3_5_salbutamol_r015_periodic_gc_attribution_observer.py' in build,
      'R015 verifier wired into build')
check('selftest_v01800_oh_rev35_r015_periodic_gc_attribution_observer.py' in prebuild,
      'R015 selftest wired into prebuild')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  '.SetValue(', '.Invoke(', 'System.Diagnostics.StackTrace',
                  'UnityEngine.Profiling.Profiler', 'System.Linq', 'new List<',
                  '.ToArray(', '.Select(', 'FlightCtrlState', 'OnFlyByWire',
                  'OnAutopilotUpdate'):
    check(forbidden not in observer, 'observer excludes ' + forbidden)

# R015 must not claim identification of external mods/processes. It establishes correlation
# and strong negative attribution only when the measured AERIS terrain heavy path is idle.
for overclaim in ('other mod caused', 'KSP caused', 'Unity caused', 'FDR caused', 'CVR caused'):
    check(overclaim not in observer, 'observer makes no unsupported attribution: ' + overclaim)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)

if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
