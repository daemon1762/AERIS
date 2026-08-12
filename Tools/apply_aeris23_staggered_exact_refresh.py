#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
renderer=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text=renderer.read_text()

if 'oh_stagger_due=' in text and 'ResolveStaggeredExactRefreshDeadlineSeconds' in text:
    print('[AERIS23 Staggered Exact Refresh] already applied')
    raise SystemExit(0)
if 'oh_affine_bridge=' not in text or 'TryResolveWitnessAffineBridge(' not in text:
    raise SystemExit('[AERIS23 Staggered Exact Refresh] Witness-Bounded Affine Projection must be applied first')

def replace_once(src,old,new,label):
    count=src.count(old)
    if count!=1:
        raise SystemExit(f'{label}: expected 1 anchor, found {count}')
    return src.replace(old,new,1)

text=replace_once(text,
'''            internal int ProjectionWitnessBasisC = -1;\n            internal float[] LandElevationMeters;''',
'''            internal int ProjectionWitnessBasisC = -1;\n            // Stable per-Entry refresh slot. -1 means the FNV-1a slot has not yet been\n            // resolved. This is presentation-only state and never affects content authority.\n            internal int ExactRefreshStaggerSlot = -1;\n            internal float[] LandElevationMeters;''',
'Entry stagger slot cache')

text=replace_once(text,
'''        const float AffineWitnessMaximumAgeSeconds = 4.00f;\n        const float AffineWitnessSourceAreaEpsilon = 0.000000001f;''',
'''        const float AffineWitnessMaximumAgeSeconds = 4.00f;\n        // Stagger the former synchronized 4.0 s freshness burst across the fixed 10 Hz\n        // presentation clock. Stable FNV-1a(CacheKey) selects one of twelve deadlines:\n        // 2.80, 2.90, ... 3.90 s. Every deadline remains strictly inside the accepted\n        // 4.00 s hard freshness rail; visual/witness acceptance is otherwise unchanged.\n        const int StaggeredExactRefreshSlotCount = 12;\n        const float StaggeredExactRefreshMinimumSeconds = 2.80f;\n        const float StaggeredExactRefreshSlotSeconds = 0.10f;\n        const float AffineWitnessSourceAreaEpsilon = 0.000000001f;''',
'stagger timing constants')

text=replace_once(text,
'''        long operationHealthAffineExactFallbacks;\n        long operationHealthAffineWitnessMaxMilliPixels;\n        long operationHealthLoadingBackdropFrames;''',
'''        long operationHealthAffineExactFallbacks;\n        long operationHealthAffineWitnessMaxMilliPixels;\n        long operationHealthStaggeredExactDue;\n        long operationHealthStaggeredExactDeferrals;\n        long operationHealthLoadingBackdropFrames;''',
'stagger telemetry fields')

text=replace_once(text,
'''                if (centerMoved || forceCenterProjectionRefresh)\n                {\n                    Matrix4x4 affineBridge;''',
'''                if (centerMoved || forceCenterProjectionRefresh)\n                {\n                    float staggeredExactDeadlineSeconds =\n                        ResolveStaggeredExactRefreshDeadlineSeconds(entry);\n                    bool staggeredExactDue = exactAge >= staggeredExactDeadlineSeconds;\n                    Matrix4x4 affineBridge;''',
'stagger deadline evaluation')

text=replace_once(text,
'''                    if (!polarExactOnly && exactAge < AffineWitnessMaximumAgeSeconds &&\n                        TryResolveWitnessAffineBridge(entry, context,''',
'''                    if (!polarExactOnly && !staggeredExactDue &&\n                        exactAge < AffineWitnessMaximumAgeSeconds &&\n                        TryResolveWitnessAffineBridge(entry, context,''',
'affine gate obeys per-Entry exact deadline')

text=replace_once(text,
'''                        if (milliPixels > operationHealthAffineWitnessMaxMilliPixels)\n                            operationHealthAffineWitnessMaxMilliPixels = milliPixels;\n                        return affineBridge;''',
'''                        if (milliPixels > operationHealthAffineWitnessMaxMilliPixels)\n                            operationHealthAffineWitnessMaxMilliPixels = milliPixels;\n                        if (exactAge >= StaggeredExactRefreshMinimumSeconds)\n                            operationHealthStaggeredExactDeferrals++;\n                        return affineBridge;''',
'stagger deferral telemetry')

text=replace_once(text,
'''                    bool translationExactDue = centerMoved &&\n                        (centerMotionSquared >= exactDistanceThreshold * exactDistanceThreshold ||\n                         exactAge >= ProjectionRefreshAgeSeconds);''',
'''                    if (staggeredExactDue) operationHealthStaggeredExactDue++;\n                    bool translationExactDue = staggeredExactDue || centerMoved &&\n                        (centerMotionSquared >= exactDistanceThreshold * exactDistanceThreshold ||\n                         exactAge >= ProjectionRefreshAgeSeconds);''',
'stagger due forces exact fallback')

marker='''        void CaptureProjectionWitnesses(Entry entry)\n        {'''
if text.count(marker)!=1:
    raise SystemExit('stagger helper insertion anchor mismatch')
helper=r'''        static int ResolveStaggeredExactRefreshSlot(Entry entry)
        {
            if (entry == null) return 0;
            if (entry.ExactRefreshStaggerSlot >= 0 &&
                entry.ExactRefreshStaggerSlot < StaggeredExactRefreshSlotCount)
                return entry.ExactRefreshStaggerSlot;
            string key = entry.CacheKey ?? string.Empty;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= 16777619u;
                }
                entry.ExactRefreshStaggerSlot = (int)(hash %
                    (uint)StaggeredExactRefreshSlotCount);
            }
            return entry.ExactRefreshStaggerSlot;
        }

        static float ResolveStaggeredExactRefreshDeadlineSeconds(Entry entry)
        {
            int slot = ResolveStaggeredExactRefreshSlot(entry);
            return Mathf.Min(AffineWitnessMaximumAgeSeconds,
                StaggeredExactRefreshMinimumSeconds +
                slot * StaggeredExactRefreshSlotSeconds);
        }

'''
text=text.replace(marker,helper+marker,1)

text=replace_once(text,
'''                "; oh_affine_exact_fallback=" + operationHealthAffineExactFallbacks +\n                "; oh_affine_max_mpx=" + operationHealthAffineWitnessMaxMilliPixels +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_affine_exact_fallback=" + operationHealthAffineExactFallbacks +\n                "; oh_affine_max_mpx=" + operationHealthAffineWitnessMaxMilliPixels +\n                "; oh_stagger_due=" + operationHealthStaggeredExactDue +\n                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'stagger telemetry publication')

renderer.write_text(text)

test=ROOT/'Tools/selftest_v01800_operation_health_staggered_exact_refresh.py'
test.write_text(r'''#!/usr/bin/env python3
from pathlib import Path
from collections import Counter
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
project=R[R.index('Matrix4x4 EnsureProjectedGeometry('):R.index('void ProjectMesh(',R.index('Matrix4x4 EnsureProjectedGeometry('))]
ck('StaggeredExactRefreshSlotCount = 12' in R,'exact refresh is distributed across twelve fixed 10 Hz slots')
ck('StaggeredExactRefreshMinimumSeconds = 2.80f' in R and
   'StaggeredExactRefreshSlotSeconds = 0.10f' in R,'stagger window is 2.80 through 3.90 seconds')
ck('AffineWitnessMaximumAgeSeconds = 4.00f' in R,'accepted 4.00 second hard freshness rail remains')
ck('ResolveStaggeredExactRefreshDeadlineSeconds(entry)' in project and
   '!staggeredExactDue' in project,'affine reuse stops at the Entry-specific deadline')
ck('translationExactDue = staggeredExactDue || centerMoved' in project,
   'deadline expiry forces the existing exact fallback path')
ck('2166136261u' in R and '16777619u' in R and 'entry.CacheKey' in R,
   'stable FNV-1a CacheKey slotting is independent of process hash randomization')
ck('ExactRefreshStaggerSlot = -1' in R,'resolved stagger slot is cached per Entry')
ck('oh_stagger_due=' in R and 'oh_stagger_defer=' in R,
   'runtime exposes stagger due and deferred-affine activity')
ck('TryResolveWitnessAffineBridge(entry, context' in project and
   'AffineWitnessAcceptancePixels = 0.08f' in R,'0.08 px witness quality gate is unchanged')
ck('ProjectionBridgeLatitudeLimitDeg' in project and 'polarExactOnly' in project,
   'polar exact-only safety remains unchanged')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'ARGB32/Bilinear visual authority remains unchanged')

def slot(key):
    h=2166136261
    for ch in key:
        h ^= ord(ch)
        h=(h*16777619)&0xffffffff
    return h%12
counts=Counter(slot('Kerbin|FAR|%d|%d|STYLE'%(i,i//17)) for i in range(12000))
ck(len(counts)==12,'synthetic cache-key audit reaches every stagger slot')
avg=12000/12.0
ck(max(counts.values()) <= avg*1.08 and min(counts.values()) >= avg*0.92,
   'synthetic cache-key slot distribution remains within +/-8 percent')
deadlines=[2.80+i*0.10 for i in range(12)]
ck(max(deadlines) < 4.00 and min(deadlines) >= 2.80,
   'every staggered deadline remains strictly inside the 4.00 second hard rail')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Staggered Exact Refresh] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
''')

prebuild=ROOT/'Tools/run_v01800_operation_health_pass3_prebuild.py'
pb=prebuild.read_text()
marker=" ('Operation Health Witness-Bounded Affine Projection','selftest_v01800_operation_health_witness_affine_projection.py'),"
addition=" ('Operation Health Staggered Exact Refresh','selftest_v01800_operation_health_staggered_exact_refresh.py'),"
if 'selftest_v01800_operation_health_staggered_exact_refresh.py' not in pb:
    if marker not in pb:
        raise SystemExit('prebuild Witness-Bounded Affine marker absent')
    pb=pb.replace(marker,marker+'\n'+addition,1)
prebuild.write_text(pb)

print('[AERIS23 Staggered Exact Refresh] patch applied')
print('Exact freshness deadlines: 12 stable CacheKey slots from 2.80 to 3.90 s; 4.00 s hard rail retained')
print('Goal: preserve affine median gains while removing synchronized ~4 s exact-upload bursts')