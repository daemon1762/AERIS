#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
BRANCH='agent/aeris33-rev3-5-r032-gilly-pure-cpu-exact-worker-poc'
MARKER='AERIS32_REV3_5_R032_PTC_GILLY_PURE_CPU_EXACT_WORKER_SHADOW'
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR032PtcGillyPureCpuExactWorkerObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
branch=subprocess.check_output(['git','branch','--show-current'],cwd=str(ROOT),text=True).strip()
if branch!=BRANCH: raise SystemExit('FAIL wrong branch '+branch)
for p in (SOURCE,CSPROJ,VERSION):
    if not p.is_file(): raise SystemExit('FAIL missing '+str(p))
text=SOURCE.read_text();cs=CSPROJ.read_text();version=VERSION.read_text()
checks=[
(MARKER in text,'marker'),
('ThreadPool.QueueUserWorkItem' in text,'bounded shared worker dispatch'),
('snapshot_payload=PRIMITIVES_ONLY' in text,'primitive-only worker snapshot contract'),
('worker_invokes_runtime_object=false' in text,'worker runtime object invocation forbidden'),
('event=PURE_CPU_EXACT_WORKER_COMPLETE' in text,'runtime completion telemetry'),
('primitive_failures=' in text and 'terrain_failures=' in text,'acceptance counters'),
('SimplexNoise(' in text and 'SimplexValue(' in text and 'FastFloor(' in text,'Simplex exact evaluator'),
('GradientNoise(' in text and 'GradientCoherentNoise(' in text,'gradient noise exact evaluator'),
('SCurve3(' in text and 'SCurve5(' in text and 'LinearInterpolate(' in text,'LibNoise interpolation closure'),
('RidgedValue(' in text,'RidgedMultifractal exact evaluator'),
('(simplex+1.0)*0.5*s.SimplexDeformity + ridged*s.RidgedDeformity' in text,'Gilly modifier composition from IL'),
('AERISR032PtcGillyPureCpuExactWorkerObserver.cs' in cs,'compiled'),
('R032 PTC GILLY PURE CPU EXACT WORKER SHADOW' in version,'build identity')]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok:failed.append(label)
if failed:raise SystemExit('FAIL: '+', '.join(failed))
for forbidden in ('SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(', 'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'ComputeShader', 'AsyncGPUReadback', 'Task.Run('):
    if forbidden in text:raise SystemExit('FAIL forbidden token '+forbidden)
worker=text[text.index('        static void EvaluateWorker'):text.index('        void Report')]
math=text[text.index('        static double SimplexNoise'):text.index('        static MethodInfo FindTripleDoubleMethod')]
for name,region in (('worker entry',worker),('pure math closure',math)):
    for forbidden in ('FlightGlobals','CelestialBody','Vector3d','UnityEngine','LibNoise','AERISLogger','ReadMember(','MethodInfo','.Invoke(','GetSurfaceHeight'):
        if forbidden in region:raise SystemExit('FAIL '+name+' touches runtime token '+forbidden)
print('[PASS] worker entry and pure math closure contain no Unity/KSP/runtime-object access')
subprocess.run(['git','diff','--check'],cwd=str(ROOT),check=True)
print('PASS: R032 Gilly pure CPU exact worker shadow verification')
print('runtime_scope=SHADOW_ONLY')
print('terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
