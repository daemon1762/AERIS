#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
SRC=ROOT/'Source/AERISFlightControl/Terrain/AERISR037PtcPolFlattenOceanExactClosureObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS33_REV3_5_R037_PTC_POL_FLATTEN_OCEAN_EXACT_CLOSURE_SHADOW'
FLATTEN_IL_SHA='4b00ff62f5a99eeae99d7236b16a0aa1dfed1d22a6c9cc991d6da38fce55a112'
PREFIX='[AERIS33 R037 POL FLATTEN OCEAN EXACT CLOSURE VERIFY]'
if not SRC.is_file(): raise SystemExit(PREFIX+' observer missing')
s=SRC.read_text(); cs=CSPROJ.read_text(); v=VERSION.read_text(); bad=[]
def check(ok,label):
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: bad.append(label)
check(MARKER in s,'marker')
check('new string[] { "Minmus", "Ike", "Gilly", "Pol" }' in s,'fixed four-body target set')
check('ThreadPool.QueueUserWorkItem' in s,'bounded worker dispatch')
check('worker_invokes_runtime_object=false' in s,'worker runtime-object isolation contract')
check('LandControlGeometryInert' in s and 'minimumRealHeight' in s and 'alterRealHeight' in s,'R035 LandControl inert guard')
check('FlattenOcean = 5' in s and 'StepKind.FlattenOcean' in s,'FlattenOcean adapter present')
check('internal double OceanRadius;' in s and 'ReadDouble(mod,"oceanRad")' in s,'oceanRad copied to pure scalar snapshot')
check(FLATTEN_IL_SHA in s,'FlattenOcean accepted IL hash guard')
check('return h<st.OceanRadius?st.OceanRadius:h;' in s,'FlattenOcean exact max semantics')
check('tn=="PQSMod_VertexSimplexHeight" || tn=="PQSMod_FlattenOcean"' in s,'signed-simplex and FlattenOcean IL audit')
check('event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE' in s,'R037 completion event')
check('Unsupported' in s and 'FORMULA_CLOSURE_PENDING' in s,'VertexPlanet remains fail-closed when unsupported')
check('db_write=false' in s and 'producer_switch=false' in s and 'gpu=false' in s and 'authority=PQS' in s,'authority isolation contract')
for token in ('SaveEncodedBatch','CommitGeneratedTile','InvalidateBodyEnvironment','DeleteBody','RequestBuild','RequestRebuild','ComputeShader','AsyncGPUReadback'):
    check(token not in s,'forbidden token absent: '+token)
check('Terrain\\AERISR037PtcPolFlattenOceanExactClosureObserver.cs' in cs,'compiled')
check(MARKER in v,'build identity')
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('runtime_scope=SHADOW_ONLY expected_worker_ready=Gilly,Ike,Pol expected_pending=Minmus authority=PQS')
