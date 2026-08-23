#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/'Source/AERISFlightControl/Terrain/AERISR038PtcMinmusVertexPlanetDependencyClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS33_REV3_5_R038_PTC_MINMUS_VERTEXPLANET_DEPENDENCY_CLOSURE_SHADOW'
MAIN_IL='513748e2fdcc9eae0ed4958840f485ad5cd6eea4efb078e81ce5ae7bd400f687'
PREFIX='[AERIS33 R038 MINMUS VERTEXPLANET DEPENDENCY CLOSURE VERIFY]'
if not SRC.is_file(): raise SystemExit(PREFIX+' observer missing')
s=SRC.read_text();cs=CSPROJ.read_text();v=VERSION.read_text();bad=[]
def check(ok,label):
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: bad.append(label)
check(MARKER in s,'marker')
check(MAIN_IL in s,'accepted VertexPlanet main IL hash guard')
check('FindBody("Minmus")' in s,'Minmus-only target')
check('PQSMod_VertexPlanet' in s,'VertexPlanet target')
check('continentalSmoothing' in s and 'continentalSharpnessMap' in s and 'continentalRuggedness' in s,'simplex wrapper inventory')
check('continentalSharpness' in s and 'GetValue' in s,'noise wrapper inventory')
check('LogHelper(mod, mt, "Lerp")' in s and 'LogHelper(mod, mt, "Clamp")' in s and 'LogHelper(mod, mt, "CubicHermite")' in s,'helper closure')
check('MemberwiseClone' in s and 'invocation_target=SHALLOW_CLONE' in s and 'live_runtime_object_mutated=false' in s,'instance helper clone isolation')
check('PQS_WITNESS' in s and 'BuildTerrainPoints' in s,'PQS witness capture')
check('worker_ready=false' in s and 'PURE_CPU_FORMULA_RECONSTRUCTION_PENDING' in s,'Minmus remains fail-closed')
check('runtime_object_invocation_thread=MAIN_THREAD_ONLY' in s and 'worker_invokes_runtime_object=false' in s,'runtime-object isolation contract')
check('db_write=false' in s and 'producer_switch=false' in s and 'gpu=false' in s and 'authority=PQS' in s,'authority isolation contract')
for token in ('SaveEncodedBatch','CommitGeneratedTile','InvalidateBodyEnvironment','DeleteBody','RequestBuild','RequestRebuild','ComputeShader','AsyncGPUReadback','ThreadPool.QueueUserWorkItem','new Thread'):
    check(token not in s,'forbidden token absent: '+token)
check('Terrain\\AERISR038PtcMinmusVertexPlanetDependencyClosureObserver.cs' in cs,'compiled')
check(MARKER in v,'build identity')
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('runtime_scope=SHADOW_ONLY parent_ready=Gilly,Ike,Pol Minmus=DEPENDENCY_CLOSURE_ONLY authority=PQS')
