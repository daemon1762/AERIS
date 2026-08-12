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

text=replace_once(text,
'''            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)''',
'''            if (staggerBurstTelemetryEligible)\n            {\n                long exactThisBack = Math.Max(0L,\n                    operationHealthProjectionExactRefreshes - exactRefreshesAtBackStart);\n                operationHealthStaggeredExactBackSamples++;\n                if (exactThisBack > operationHealthStaggeredExactBackPeak)\n                    operationHealthStaggeredExactBackPeak = exactThisBack;\n                if (exactThisBack > 8L) operationHealthStaggeredExactBackOverEight++;\n            }\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)''',
'measure steady exact count per BACK')

text=replace_once(text,
'''                "; oh_stagger_due=" + operationHealthStaggeredExactDue +\n                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_stagger_due=" + operationHealthStaggeredExactDue +\n                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +\n                "; oh_stagger_back_peak=" + operationHealthStaggeredExactBackPeak +\n                "; oh_stagger_back_samples=" + operationHealthStaggeredExactBackSamples +\n                "; oh_stagger_back_gt8=" + operationHealthStaggeredExactBackOverEight +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'publish steady exact burst telemetry')
path.write_text(text)
print('[AERIS23 Stagger Burst Telemetry] applied')
print('READY steady-state telemetry: exact Entry count per BACK peak and >8 burst frequency')