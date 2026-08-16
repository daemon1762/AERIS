#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
P = ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'
P_RETAINED = ROOT / 'Tools/selftest_v01800_operation_health_retained_surface.py'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_002 SELFTEST]'


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch old=%d' % (PREFIX, label, count))
    return text.replace(old, new, 1), True


# Step2 historically required worker completion drain to occur only on a content tick.
# Phase6_002 still keeps CaptureVisible/projection/BACK on the authoritative 10 Hz path,
# but permits the hidden staged commit pump to advance between authoritative ticks.
text = P.read_text()
old1 = "ck('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw,'worker completion wakes content maintenance')"
new1 = """phase6_staged = ('AERIS25_STAGED_MAIN_THREAD_COMMIT' in R and
    'NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B)
ck(('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw) or
   (phase6_staged and
    'bool workerResultReady = pendingEntryCommit != null || rasterizer.CompletedCount > 0;' in draw),
   'worker completion or exact Phase6_002 pending commit wakes content maintenance')"""
text, c1 = replace_once(text, old1, new1, 'worker-ready successor')

old2 = "ck('DrainCompleted(system);' in content and 'system.CaptureVisible(' in content,'worker drain and visible capture are content-only work')"
new2 = """ck((('DrainCompleted(system);' in content) or
    (phase6_staged and 'PumpStagedCompletedCommit(system);' in content)) and
   'system.CaptureVisible(' in content,
   'content tick owns visible capture and legacy drain or exact Phase6_002 staged pump')"""
text, c2 = replace_once(text, old2, new2, 'content-pump successor')

if c1 or c2:
    P.write_text(text)
    print(PREFIX + ' exact Phase6_002 Step2 successor contract applied')
else:
    print(PREFIX + ' exact Phase6_002 Step2 successor contract already present')

# Retained FRONT historically required an absolutely zero-work early gate before the first
# resident-cache read. Phase6_002 intentionally performs only bounded hidden commit work
# before presenting the retained FRONT. Admit only this exact revision and continue to
# require that no CaptureVisible/resource/render/presentation rebuild work occurs there.
retained = P_RETAINED.read_text()
retained, r0 = replace_once(
    retained,
    "S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()",
    "S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()\nB=(ROOT/'build_ubuntu.sh').read_text()",
    'retained selftest build identity source')
old_fast = "fast=draw[draw.index('float presentationNow'):draw.index('residentCache = system.CurrentBodyResidentCache;')]"
new_fast = """phase6_staged = ('AERIS25_STAGED_MAIN_THREAD_COMMIT' in R and
    'NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B)
fast=(draw[draw.index('if (!authoritativeTickDue)'):draw.index('AERISTerrainGpuMode currentGpuMode')]
      if phase6_staged else
      draw[draw.index('float presentationNow'):draw.index('residentCache = system.CurrentBodyResidentCache;')])"""
retained, r1 = replace_once(retained, old_fast, new_fast,
                            'retained fast-region successor')
old_gate = "ck('TryPresentCoalescedFront(plot, vessel)' in fast,'retained FRONT gate exists before normal renderer work')"
new_gate = """ck('TryPresentCoalescedFront(plot, vessel)' in fast and
   ((not phase6_staged) or 'PumpStagedCompletedCommit(system);' in fast),
   'retained FRONT gate exists before normal renderer work; exact Phase6_002 may run only staged commit first')"""
retained, r2 = replace_once(retained, old_gate, new_gate,
                            'retained gate successor')
old_order = "ck(draw.index('TryPresentCoalescedFront(plot, vessel)') < draw.index('residentCache = system.CurrentBodyResidentCache;'),'retained gate precedes resident-cache access')"
new_order = """legacy_resident_order = (draw.index('TryPresentCoalescedFront(plot, vessel)') <
    draw.index('residentCache = system.CurrentBodyResidentCache;'))
phase6_resident_order = (phase6_staged and
    fast.count('residentCache = system.CurrentBodyResidentCache;') >= 2 and
    fast.index('residentCache = system.CurrentBodyResidentCache;') <
        fast.index('PumpStagedCompletedCommit(system);') <
        fast.index('TryPresentCoalescedFront(plot, vessel)') <
        fast.rfind('residentCache = system.CurrentBodyResidentCache;'))
ck(legacy_resident_order or phase6_resident_order,
   'retained gate precedes normal authoritative resident-cache access; exact Phase6_002 staged pump may borrow cache first')"""
retained, r3 = replace_once(retained, old_order, new_order,
                            'retained resident ordering successor')
old_work = "ck('CaptureVisible' not in fast and 'DrainCompleted' not in fast and 'EnsureResources' not in fast,'retained gate performs no content/resource work')"
new_work = """ck('CaptureVisible' not in fast and 'DrainCompleted' not in fast and
   'EnsureResources' not in fast and 'RenderBackBuffer' not in fast and
   ((not phase6_staged) or fast.count('PumpStagedCompletedCommit(system);') == 1),
   'retained gate performs no visible content/resource/render work; exact Phase6_002 permits one hidden staged pump')"""
retained, r4 = replace_once(retained, old_work, new_work,
                            'retained work successor')
if any((r0, r1, r2, r3, r4)):
    P_RETAINED.write_text(retained)
    print(PREFIX + ' exact Phase6_002 retained FRONT successor contract applied')
else:
    print(PREFIX + ' exact Phase6_002 retained FRONT successor contract already present')

print('Invariant: visible CaptureVisible/projection/BACK presentation remain authoritative-tick work; only staged commit may advance between ticks')
