#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
BRANCH='agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
MARKER='AERIS32_REV3_5_R031_PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW'
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR031PtcGillyDependencyClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
branch=subprocess.check_output(['git','branch','--show-current'],cwd=str(ROOT),text=True).strip()
if branch!=BRANCH: raise SystemExit('FAIL wrong branch '+branch)
for p in (SOURCE,CSPROJ,VERSION):
    if not p.is_file(): raise SystemExit('FAIL missing '+str(p))
text=SOURCE.read_text(); cs=CSPROJ.read_text(); version=VERSION.read_text()
checks=[
(MARKER in text,'marker'),
('LibNoise.GradientNoiseBasis' in text and 'LibNoise.Utils' in text,'LibNoise dependency targets'),
('[R031][PTC_GILLY_DEP_METHOD]' in text,'method telemetry'),
('[R031][PTC_GILLY_DEP_IL]' in text,'IL telemetry'),
('[R031][PTC_GILLY_DEP_DATA]' in text,'array telemetry'),
('[R031][PTC_GILLY_DEP] event=DEPENDENCY_CLOSURE_COMPLETE' in text,'summary telemetry'),
('Simplex.perm' in text and 'Simplex.grad3' in text,'Simplex full data snapshot'),
('RidgedMultifractal.SpectralWeights' in text,'ridged weights snapshot'),
('worker_invokes_runtime_object=false' in text,'worker runtime object invocation forbidden'),
('authority=PQS' in text,'PQS authority'),
('AERISR031PtcGillyDependencyClosureObserver.cs' in cs,'compiled'),
('R031 PTC GILLY DEPENDENCY CLOSURE SHADOW' in version,'build identity')]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: failed.append(label)
if failed: raise SystemExit('FAIL: '+', '.join(failed))
for forbidden in ('SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(', 'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'ComputeShader', 'AsyncGPUReadback', 'Task.Run(', 'ThreadPool.QueueUserWorkItem'):
    if forbidden in text: raise SystemExit('FAIL forbidden token '+forbidden)
subprocess.run(['git','diff','--check'],cwd=str(ROOT),check=True)
print('PASS: R031 Gilly dependency closure shadow verification')
print('runtime_scope=OBSERVATION_ONLY')
print('terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
