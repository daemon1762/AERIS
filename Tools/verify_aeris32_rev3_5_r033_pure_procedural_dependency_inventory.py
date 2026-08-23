#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
SRC=ROOT/'Source/AERISFlightControl/Terrain/AERISR033PtcPureProceduralDependencyInventoryObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS32_REV3_5_R033_PTC_PURE_PROCEDURAL_DEPENDENCY_INVENTORY_SHADOW'
PREFIX='[AERIS32 R033 PURE PROCEDURAL INVENTORY VERIFY]'
if not SRC.is_file():raise SystemExit(PREFIX+' missing generated observer; run apply first')
s=SRC.read_text();cs=CSPROJ.read_text();v=VERSION.read_text();bad=[]
def check(ok,label):
 print(('[PASS] ' if ok else '[FAIL] ')+label)
 if not ok:bad.append(label)
check(MARKER in s,'marker')
check('new string[]{"Minmus","Ike","Gilly","Pol"}' in s,'fixed four-body target set')
check('AERISTerrainTileSystem.GameDataHashReady' in s and 'FlightGlobals.Bodies' in s,'runtime-world readiness gate')
check('OnVertexBuildHeight' in s and 'BindingFlags.DeclaredOnly' in s,'declared height contributor discovery')
check('System.Reflection.Emit' in s and 'OpCodes' in s and 'GetILAsByteArray' in s and 'OperandSize' in s,'bounded IL dependency parser')
check('ResolveMethodSafe' in s and 'ResolveFieldSafe' in s and 'ResolveTypeSafe' in s,'method/field/type token resolution')
check('MaxMethodsPerBody=256' in s and 'MaxObjectsPerBody=128' in s and 'MaxObjectDepth=4' in s and 'MaxArrayValues=4096' in s,'bounded dependency inventory')
check('[R033][PTC_PROC_OBJECT]' in s and '[R033][PTC_PROC_ARRAY]' in s and '[R033][PTC_PROC_METHOD]' in s,'object/array/method telemetry')
check('event=PURE_PROCEDURAL_INVENTORY_COMPLETE' in s,'runtime completion telemetry')
check('runtime_object_invocation_thread=MAIN_THREAD_ONLY' in s and 'worker_dispatch=false' in s and 'worker_invokes_runtime_object=false' in s,'main-thread-only inventory contract')
check('db_write=false' in s and 'producer_switch=false' in s and 'gpu=false' in s and 'authority=PQS' in s,'authority isolation contract')
for token in ('ThreadPool','Task.Run','SaveEncodedBatch','CommitGeneratedTile','InvalidateBodyEnvironment','DeleteBody','RequestBuild','RequestRebuild','ComputeShader','AsyncGPUReadback'):
 check(token not in s,'forbidden token absent: '+token)
check('Terrain\\AERISR033PtcPureProceduralDependencyInventoryObserver.cs' in cs,'compiled')
check(MARKER in v,'build identity')
if bad:raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('runtime_scope=SHADOW_ONLY')
print('targets=Minmus,Ike,Gilly,Pol worker_dispatch=NO terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
