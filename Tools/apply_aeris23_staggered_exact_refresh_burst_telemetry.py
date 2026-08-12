#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
path=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text=path.read_text()
if 'oh_stagger_back_peak=' in text:
    print('[AERIS23 Stagger Burst Telemetry] already applied')
    raise SystemExit(0)

def replace_once(src,old,new,label):
    count=src.count(old)
    if count!=1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old,new,1)

text=replace_once(text,
'''        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthLoadingBackdropFrames;''',
'''        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthStaggeredExactBackPeak;\n        long operationHealthStaggeredExactBackSamples;\n        long operationHealthStaggeredExactBackOverEight;\n        long operationHealthLoadingBackdropFrames;''',
'stagger burst telemetry fields')

text=replace_once(text,
'''            long frameStartTicks = Stopwatch.GetTimestamp();\n            RenderTexture previous = RenderTexture.active;''',
'''            long frameStartTicks = Stopwatch.GetTimestamp();\n            long exactRefreshesAtBackStart = operationHealthProjectionExactRefreshes;\n            bool staggerBurstTelemetryEligible = frontBufferValid && requestedViewReady;\n            RenderTexture previous = RenderTexture.active;''',
'capture exact count at BACK start')

# Do not anchor on the generic AERISPerformanceRuntime block alone: the current
# renderer legitimately contains several such blocks.  Bind this insertion to the
# unique RenderBackBuffer tail that records frameStartTicks and immediately returns
# the rendered result.  If that exact tail moves, fail closed instead of guessing.
text=replace_once(text,
'''            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)\n                runtime.Gpu.RecordFrameCost((Stopwatch.GetTimestamp() - frameStartTicks) *\n                    1000.0 / Stopwatch.Frequency);\n            return rendered;''',
'''            if (staggerBurstTelemetryEligible)\n            {\n                long exactThisBack = Math.Max(0L,\n                    operationHealthProjectionExactRefreshes - exactRefreshesAtBackStart);\n                operationHealthStaggeredExactBackSamples++;\n                if (exactThisBack > operationHealthStaggeredExactBackPeak)\n                    operationHealthStaggeredExactBackPeak = exactThisBack;\n                if (exactThisBack > 8L) operationHealthStaggeredExactBackOverEight++;\n            }\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)\n                runtime.Gpu.RecordFrameCost((Stopwatch.GetTimestamp() - frameStartTicks) *\n                    1000.0 / Stopwatch.Frequency);\n            return rendered;''',
'measure steady exact count at unique BACK tail')

text=replace_once(text,
'''                "; oh_stagger_due=" + operationHealthStaggeredExactDue +\n                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_stagger_due=" + operationHealthStaggeredExactDue +\n                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +\n                "; oh_stagger_back_peak=" + operationHealthStaggeredExactBackPeak +\n                "; oh_stagger_back_samples=" + operationHealthStaggeredExactBackSamples +\n                "; oh_stagger_back_gt8=" + operationHealthStaggeredExactBackOverEight +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'publish steady exact burst telemetry')
path.write_text(text)
print('[AERIS23 Stagger Burst Telemetry] applied')
print('READY steady-state telemetry: exact Entry count per BACK peak and >8 burst frequency')
