#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
SRC=ROOT/'Source/AERISFlightControl/Terrain/AERISR035LandControlWriteSemanticsIlAuditObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS32_REV3_5_R035_LANDCONTROL_WRITE_SEMANTICS_IL_AUDIT_SHADOW'
PREFIX='[AERIS32 R035 LANDCONTROL WRITE SEMANTICS IL AUDIT VERIFY]'
if not SRC.is_file(): raise SystemExit(PREFIX+' missing observer')
s=SRC.read_text();cs=CSPROJ.read_text();v=VERSION.read_text();bad=[]
def check(ok,label):
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: bad.append(label)
check(MARKER in s,'marker')
check('new string[] { "Minmus", "Ike", "Gilly", "Pol" }' in s,'fixed four-body config target set')
check('AERISTerrainTileSystem.GameDataHashReady' in s and 'FlightGlobals.Bodies' in s,'runtime-world readiness gate')
check('OnVertexBuildHeight' in s and 'GetILAsByteArray' in s,'runtime IL source')
check('[R035][LANDCTRL_IL_INSN]' in s and 'vertHeight_write=' in s,'full instruction telemetry')
check('InlineBrTarget' in s and 'InlineSwitch' in s and 'ResolveField' in s and 'ResolveMethod' in s,'resolved control-flow/member operands')
check('nonzero_alterRealHeight' in s and 'nonzero_alterApparentHeight' in s and 'nonzero_minimumRealHeight' in s,'runtime config zero/nonzero audit')
check('runtime_object_invocation_thread=MAIN_THREAD_ONLY' in s and 'worker_dispatch=false' in s and 'worker_invokes_runtime_object=false' in s,'main-thread-only contract')
check('db_write=false' in s and 'producer_switch=false' in s and 'gpu=false' in s and 'authority=PQS' in s,'authority isolation contract')
for token in ('ThreadPool','Task.Run','SaveEncodedBatch','CommitGeneratedTile','InvalidateBodyEnvironment','DeleteBody','RequestBuild','RequestRebuild','ComputeShader','AsyncGPUReadback'):
    check(token not in s,'forbidden token absent: '+token)
check('Terrain\\AERISR035LandControlWriteSemanticsIlAuditObserver.cs' in cs,'compiled')
check(MARKER in v,'build identity')
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('runtime_scope=SHADOW_ONLY IL_READ_ONLY=YES targets=Minmus,Ike,Gilly,Pol worker_dispatch=NO terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
