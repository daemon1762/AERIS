#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
ORIGINAL = ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution.py'
PREFIX = '[AERIS27 R007 APPLY V2]'

if not ORIGINAL.is_file():
    raise SystemExit(PREFIX + ' original apply script missing')
source = ORIGINAL.read_text()


def patch_once(old, new, label):
    global source
    count = source.count(old)
    if count != 1:
        raise SystemExit(PREFIX + ' script-transform anchor mismatch ' + label + '=' + str(count))
    source = source.replace(old, new, 1)

# The accepted HF3 generated renderer has inherited instrumentation between these calls,
# so R007 v1's multi-line context anchors were too strict. Use the unique call/counter
# lines themselves; generated behavior is identical to the original R007 design.
patch_once(
'''renderer, _ = replace_once(
    renderer,
    '                operationHealthContentTicks++;\\n'
    '                if (workerResultReady) operationHealthContentWorkerDrains++;\\n'
    '                DrainCompleted(system);\\n',
    '                operationHealthContentTicks++;\\n'
    '                if (workerResultReady) operationHealthContentWorkerDrains++;\\n'
    '                BeginRev35R007HeapWindow();\\n'
    '                DrainCompleted(system);\\n'
    '                ObserveRev35R007HeapWindow(\\n'
    '                    ref operationHealthRev35R007DrainSamples,\\n'
    '                    ref operationHealthRev35R007DrainPositiveBytes,\\n'
    '                    ref operationHealthRev35R007DrainPositiveMaxBytes);\\n',
    'R007 drain attribution')
''',
'''renderer, _ = replace_once(
    renderer,
    '                DrainCompleted(system);\\n',
    '                BeginRev35R007HeapWindow();\\n'
    '                DrainCompleted(system);\\n'
    '                ObserveRev35R007HeapWindow(\\n'
    '                    ref operationHealthRev35R007DrainSamples,\\n'
    '                    ref operationHealthRev35R007DrainPositiveBytes,\\n'
    '                    ref operationHealthRev35R007DrainPositiveMaxBytes);\\n',
    'R007 drain attribution')
''', 'drain')

patch_once(
'''renderer, _ = replace_once(
    renderer,
    '                operationHealthContentCaptures++;\\n'
    '                if (visible == null || visible.Tiles == null ||\\n',
    '                operationHealthContentCaptures++;\\n'
    '                ObserveRev35R007HeapWindow(\\n'
    '                    ref operationHealthRev35R007CaptureSamples,\\n'
    '                    ref operationHealthRev35R007CapturePositiveBytes,\\n'
    '                    ref operationHealthRev35R007CapturePositiveMaxBytes);\\n'
    '                if (visible == null || visible.Tiles == null ||\\n',
    'R007 capture attribution')
''',
'''renderer, _ = replace_once(
    renderer,
    '                operationHealthContentCaptures++;\\n',
    '                operationHealthContentCaptures++;\\n'
    '                ObserveRev35R007HeapWindow(\\n'
    '                    ref operationHealthRev35R007CaptureSamples,\\n'
    '                    ref operationHealthRev35R007CapturePositiveBytes,\\n'
    '                    ref operationHealthRev35R007CapturePositiveMaxBytes);\\n',
    'R007 capture attribution')
''', 'capture')

patch_once(
'''renderer, _ = replace_once(
    renderer,
    '            if (contentTickRequired)\\n'
    '            {\\n'
    '                Prune(ResolveVramLimitBytes());\\n'
    '                PruneRenderReady(ResolveRenderReadyLimitBytes());\\n'
    '            }\\n',
    '            if (contentTickRequired)\\n'
    '            {\\n'
    '                BeginRev35R007HeapWindow();\\n'
    '                Prune(ResolveVramLimitBytes());\\n'
    '                PruneRenderReady(ResolveRenderReadyLimitBytes());\\n'
    '                ObserveRev35R007HeapWindow(\\n'
    '                    ref operationHealthRev35R007PruneSamples,\\n'
    '                    ref operationHealthRev35R007PrunePositiveBytes,\\n'
    '                    ref operationHealthRev35R007PrunePositiveMaxBytes);\\n'
    '            }\\n',
    'R007 prune attribution')
''',
'''renderer, _ = replace_once(
    renderer,
    '                Prune(ResolveVramLimitBytes());\\n',
    '                BeginRev35R007HeapWindow();\\n'
    '                Prune(ResolveVramLimitBytes());\\n',
    'R007 prune begin')
renderer, _ = replace_once(
    renderer,
    '                PruneRenderReady(ResolveRenderReadyLimitBytes());\\n',
    '                PruneRenderReady(ResolveRenderReadyLimitBytes());\\n'
    '                ObserveRev35R007HeapWindow(\\n'
    '                    ref operationHealthRev35R007PruneSamples,\\n'
    '                    ref operationHealthRev35R007PrunePositiveBytes,\\n'
    '                    ref operationHealthRev35R007PrunePositiveMaxBytes);\\n',
    'R007 prune end')
''', 'prune')

namespace = {
    '__name__': '__main__',
    '__file__': str(ORIGINAL),
    '__package__': None,
}
exec(compile(source, str(ORIGINAL), 'exec'), namespace, namespace)
