#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
SRC=ROOT/'Source/AERISFlightControl/Terrain/AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS33_REV3_5_R036_PTC_COMMON_PURE_CPU_EXACT_FORMULA_CLOSURE_SHADOW'
PREFIX='[AERIS33 R036 COMMON PURE CPU EXACT FORMULA CLOSURE VERIFY]'
if not SRC.is_file(): raise SystemExit(PREFIX+' observer missing')
s=SRC.read_text(); cs=CSPROJ.read_text(); v=VERSION.read_text(); bad=[]
def check(ok,label):
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: bad.append(label)
check(MARKER in s,'marker')
check('new string[] { "Minmus", "Ike", "Gilly", "Pol" }' in s,'fixed four-body target set')
check('ThreadPool.QueueUserWorkItem' in s,'bounded common worker dispatch')
check('snapshot_payload=PRIMITIVES_ONLY' in s,'primitive-only snapshot contract')
check('worker_invokes_runtime_object=false' in s,'worker runtime-object isolation contract')
check('LandControlGeometryInert' in s and 'minimumRealHeight' in s and 'alterRealHeight' in s,'R035 LandControl inert guard')
check('StepKind.SimplexAbsolute' in s and 'StepKind.RidgedHeight' in s and 'StepKind.SimplexSigned' in s and 'StepKind.HeightOffset' in s,'common supported adapter set')
check('PQSMod_VertexSimplexHeight' in s and 'LogIlClosure' in s,'signed simplex formula audit')
check('Unsupported' in s and 'FORMULA_CLOSURE_PENDING' in s,'unsupported contributor fail-closed path')
check('db_write=false' in s and 'producer_switch=false' in s and 'gpu=false' in s and 'authority=PQS' in s,'authority isolation contract')
for token in ('SaveEncodedBatch','CommitGeneratedTile','InvalidateBodyEnvironment','DeleteBody','RequestBuild','RequestRebuild','ComputeShader','AsyncGPUReadback'):
    check(token not in s,'forbidden token absent: '+token)
check('Terrain\\AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs' in cs,'compiled')
check(MARKER in v,'build identity')
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('runtime_scope=SHADOW_ONLY expected_worker_ready=Gilly,Ike expected_pending=Minmus,Pol authority=PQS')
