#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / 'Tools/apply_aeris24_gpu_vertex_projection_poc.py'
text = path.read_text()


def replace_once(src, old, new, label):
    if new in src:
        print('[PASS] ' + label + ' already applied')
        return src
    count = src.count(old)
    if count != 1:
        raise SystemExit('[AERIS24 GPU VERTEX FIX] %s: expected 1 anchor, found %d' %
                         (label, count))
    print('[PASS] ' + label)
    return src.replace(old, new, 1)

# PR26 burst telemetry sits between Stagger due/defer and LoadingBackdrop in the final
# PENICILLIN-generated renderer. Preserve those fields rather than anchoring to the older
# pre-burst layout.
old_fields = """'''        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthLoadingBackdropFrames;''',\n'''        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthGpuVertexAttributeUploads;\n        long operationHealthGpuVertexAttributeFailures;\n        long operationHealthGpuVertexExactBypasses;\n        long operationHealthGpuVertexBackFrames;\n        long operationHealthGpuVertexDraws;\n        long operationHealthLoadingBackdropFrames;''',"""
new_fields = """'''        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthStaggeredExactBackPeak;\n        long operationHealthStaggeredExactBackSamples;\n        long operationHealthStaggeredExactBackOverEight;\n        long operationHealthLoadingBackdropFrames;''',\n'''        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthStaggeredExactBackPeak;\n        long operationHealthStaggeredExactBackSamples;\n        long operationHealthStaggeredExactBackOverEight;\n        long operationHealthGpuVertexAttributeUploads;\n        long operationHealthGpuVertexAttributeFailures;\n        long operationHealthGpuVertexExactBypasses;\n        long operationHealthGpuVertexBackFrames;\n        long operationHealthGpuVertexDraws;\n        long operationHealthLoadingBackdropFrames;''',"""
text = replace_once(text, old_fields, new_fields, 'final Stagger burst field anchor')

old_pub = """'''                \"; oh_stagger_due=\" + operationHealthStaggeredExactDue +\n                \"; oh_stagger_defer=\" + operationHealthStaggeredExactDeferrals +\n                \"; oh_loading_backdrop=\" + operationHealthLoadingBackdropFrames +''',\n'''                \"; oh_stagger_due=\" + operationHealthStaggeredExactDue +\n                \"; oh_stagger_defer=\" + operationHealthStaggeredExactDeferrals +\n                \"; oh_gpu_vertex_projection=\" +\n                    (gpuVertexProjection.Active ? \"ACTIVE\" : \"CPU_FALLBACK\") +\n                \"; oh_gpu_vertex_attr_upload=\" + operationHealthGpuVertexAttributeUploads +\n                \"; oh_gpu_vertex_attr_fail=\" + operationHealthGpuVertexAttributeFailures +\n                \"; oh_gpu_vertex_exact_bypass=\" + operationHealthGpuVertexExactBypasses +\n                \"; oh_gpu_vertex_back_frames=\" + operationHealthGpuVertexBackFrames +\n                \"; oh_gpu_vertex_draws=\" + operationHealthGpuVertexDraws +\n                \"; oh_loading_backdrop=\" + operationHealthLoadingBackdropFrames +''',"""
new_pub = """'''                \"; oh_stagger_due=\" + operationHealthStaggeredExactDue +\n                \"; oh_stagger_defer=\" + operationHealthStaggeredExactDeferrals +\n                \"; oh_stagger_back_peak=\" + operationHealthStaggeredExactBackPeak +\n                \"; oh_stagger_back_samples=\" + operationHealthStaggeredExactBackSamples +\n                \"; oh_stagger_back_gt8=\" + operationHealthStaggeredExactBackOverEight +\n                \"; oh_loading_backdrop=\" + operationHealthLoadingBackdropFrames +''',\n'''                \"; oh_stagger_due=\" + operationHealthStaggeredExactDue +\n                \"; oh_stagger_defer=\" + operationHealthStaggeredExactDeferrals +\n                \"; oh_stagger_back_peak=\" + operationHealthStaggeredExactBackPeak +\n                \"; oh_stagger_back_samples=\" + operationHealthStaggeredExactBackSamples +\n                \"; oh_stagger_back_gt8=\" + operationHealthStaggeredExactBackOverEight +\n                \"; oh_gpu_vertex_projection=\" +\n                    (gpuVertexProjection.Active ? \"ACTIVE\" : \"CPU_FALLBACK\") +\n                \"; oh_gpu_vertex_attr_upload=\" + operationHealthGpuVertexAttributeUploads +\n                \"; oh_gpu_vertex_attr_fail=\" + operationHealthGpuVertexAttributeFailures +\n                \"; oh_gpu_vertex_exact_bypass=\" + operationHealthGpuVertexExactBypasses +\n                \"; oh_gpu_vertex_back_frames=\" + operationHealthGpuVertexBackFrames +\n                \"; oh_gpu_vertex_draws=\" + operationHealthGpuVertexDraws +\n                \"; oh_loading_backdrop=\" + operationHealthLoadingBackdropFrames +''',"""
text = replace_once(text, old_pub, new_pub, 'final Stagger burst publication anchor')

# Dispose the shader backend only after ReleaseGpuResources has completed its ordinary
# Terrain resource release. This avoids re-opening backend lifecycle state after Dispose.
old_dispose = """    text = replace_once(text,\n'''            disposed = true;\n            rasterizer.Dispose();''',\n'''            disposed = true;\n            gpuVertexProjection.Dispose();\n            rasterizer.Dispose();''',\n'GPU projection final dispose')"""
new_dispose = """    text = replace_once(text,\n'''            disposed = true;\n            rasterizer.Dispose();\n            ReleaseGpuResources();''',\n'''            disposed = true;\n            rasterizer.Dispose();\n            ReleaseGpuResources();\n            gpuVertexProjection.Dispose();''',\n'GPU projection final dispose')"""
text = replace_once(text, old_dispose, new_dispose, 'backend final-dispose ordering')

path.write_text(text)
print('[AERIS24 GPU VERTEX FIX] final PENICILLIN/Stagger anchor alignment PASS')
