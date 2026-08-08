#!/usr/bin/env python3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
R=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
P=ROOT/'Tools/run_v01800_operation_health_pass3_prebuild.py'
text=R.read_text()

old='''        static readonly Bounds NdPresentationBounds = new Bounds(\n            new Vector3(0.5f, 0.5f, 0f), new Vector3(32f, 32f, 4f));\n'''
new='''        static readonly Bounds NdPresentationBounds = new Bounds(\n            new Vector3(0.5f, 0.5f, 0f), new Vector3(32f, 32f, 4f));\n        static readonly Rect FrontUvDirect = new Rect(0f, 0f, 1f, 1f);\n        static readonly Rect FrontUvFlipped = new Rect(0f, 1f, 1f, -1f);\n'''
assert old in text
text=text.replace(old,new,1)

old='''        string frontBodyName = string.Empty;\n        long frontBodyRadiusMillimetres;\n'''
new='''        string frontBodyName = string.Empty;\n        long frontBodyRadiusMillimetres;\n        // AERIS23 FRONT presentation fast path: exact body object identity is captured\n        // only on authoritative FRONT swap. Non-authoritative IMGUI Repaints can then\n        // validate the committed surface without repeated string/radius work.\n        CelestialBody frontBodyReference;\n'''
assert old in text
text=text.replace(old,new,1)

old='''            frontBodyName = visible.BodyName ?? string.Empty;\n            frontBodyRadiusMillimetres = vessel == null || vessel.mainBody == null ? 0L :\n                (long)Math.Round(Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);\n'''
new='''            frontBodyName = visible.BodyName ?? string.Empty;\n            frontBodyReference = vessel == null ? null : vessel.mainBody;\n            frontBodyRadiusMillimetres = vessel == null || vessel.mainBody == null ? 0L :\n                (long)Math.Round(Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);\n'''
assert old in text
text=text.replace(old,new,1)

old='''        bool TryPresentCoalescedFront(Rect plot, Vessel vessel)\n        {\n            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||\n                vessel == null || vessel.mainBody == null) return false;\n            if (!string.Equals(frontBodyName, vessel.mainBody.name,\n                    StringComparison.OrdinalIgnoreCase)) return false;\n            long bodyRadiusMillimetres = (long)Math.Round(\n                Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);\n            if (bodyRadiusMillimetres != frontBodyRadiusMillimetres) return false;\n            PresentFrontDirect(plot, frontOrientation);\n            lastFrontBufferPresented = true;\n            lastFrontBufferLatched = true;\n            CapturePresentedProjection(true);\n            lastVisualCoverageFraction = 1f;\n            operationHealthCoalescedPresentFrames++;\n            if (!requestedViewReady) operationHealthLoadingBackdropFrames++;\n            return true;\n        }\n'''
new='''        bool TryPresentCoalescedFront(Rect plot, Vessel vessel)\n        {\n            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||\n                vessel == null || vessel.mainBody == null ||\n                !ReferenceEquals(frontBodyReference, vessel.mainBody)) return false;\n            // Non-authoritative Repaint must still place the retained texture once because\n            // Unity IMGUI rebuilds the framebuffer every rendered frame. Everything else\n            // reuses state established by the 10 Hz authoritative FRONT commit.\n            PresentFrontDirect(plot, frontOrientation);\n            lastFrontBufferPresented = true;\n            lastFrontBufferLatched = true;\n            presentedProjection.Valid = true;\n            presentedProjection.Latched = true;\n            presentedProjection.AgeSeconds = Math.Max(0f,\n                Time.realtimeSinceStartup - frontCommittedRealtime);\n            lastVisualCoverageFraction = 1f;\n            operationHealthCoalescedPresentFrames++;\n            if (!requestedViewReady) operationHealthLoadingBackdropFrames++;\n            return true;\n        }\n'''
assert old in text
text=text.replace(old,new,1)

old='''        void PresentFrontDirect(Rect plot,\n            AERISTerrainRenderTargetOrientation orientation)\n        {\n            bool flipVertically = orientation ==\n                AERISTerrainRenderTargetOrientation.Flipped;\n            Rect uv = flipVertically ? new Rect(0f, 1f, 1f, -1f) :\n                new Rect(0f, 0f, 1f, 1f);\n            GUI.DrawTextureWithTexCoords(plot, frontTarget, uv, true);\n        }\n'''
new='''        void PresentFrontDirect(Rect plot,\n            AERISTerrainRenderTargetOrientation orientation)\n        {\n            Rect uv = orientation == AERISTerrainRenderTargetOrientation.Flipped ?\n                FrontUvFlipped : FrontUvDirect;\n            GUI.DrawTextureWithTexCoords(plot, frontTarget, uv, true);\n        }\n'''
assert old in text
text=text.replace(old,new,1)

old='''            frontBodyName = string.Empty;\n            frontBodyRadiusMillimetres = 0L;\n'''
new='''            frontBodyName = string.Empty;\n            frontBodyRadiusMillimetres = 0L;\n            frontBodyReference = null;\n'''
assert old in text
text=text.replace(old,new,1)
R.write_text(text)

selftest=ROOT/'Tools/selftest_v01800_operation_health_front_presentation_fastpath.py'
selftest.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in (ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text(),'authoritative ND contract remains 10 Hz')
ck('CelestialBody frontBodyReference;' in R,'FRONT captures body object identity')
swap=R[R.index('void SwapFrontAndBack('):R.index('bool IsFrontBufferCompatible(')]
ck('frontBodyReference = vessel == null ? null : vessel.mainBody;' in swap,'body identity is captured only on authoritative FRONT swap')
fast=R[R.index('bool TryPresentCoalescedFront('):R.index('void MarkGpuContentDirty(')]
ck('ReferenceEquals(frontBodyReference, vessel.mainBody)' in fast,'non-authoritative path uses constant-time body identity check')
ck('string.Equals' not in fast and 'Math.Round' not in fast,'non-authoritative path removes body string/radius recomputation')
ck('CapturePresentedProjection(' not in fast,'non-authoritative path does not recopy full projection snapshot')
ck('presentedProjection.Valid = true;' in fast and 'presentedProjection.Latched = true;' in fast,'non-authoritative path reuses committed projection state')
ck(fast.count('PresentFrontDirect(') == 1,'IMGUI continuity path performs exactly one unavoidable retained-FRONT blit')
present=R[R.index('void PresentFrontDirect('):R.index('bool TryPresentReprojectedFront(')]
ck('FrontUvFlipped' in present and 'FrontUvDirect' in present and 'new Rect' not in present,'FRONT UV rectangles are cached')
reset=R[R.index('void ResetFrontBufferState('):R.index('void Schedule(')]
ck('frontBodyReference = null;' in reset,'full FRONT reset clears cached body identity')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'authoritative cadence remains fixed at 0.10 seconds')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'visual quality authority remains ARGB32 Bilinear')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health FRONT Presentation Fast Path] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

pre=P.read_text()
needle=" ('Operation Health Step 2 Motion Content Split + Coastal Edge Refinement','selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'),\n"
assert needle in pre
pre=pre.replace(needle,needle+" ('Operation Health FRONT Presentation Fast Path','selftest_v01800_operation_health_front_presentation_fastpath.py'),\n",1)
P.write_text(pre)
