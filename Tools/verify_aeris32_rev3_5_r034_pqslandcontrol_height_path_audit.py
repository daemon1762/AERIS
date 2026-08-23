#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
SRC=ROOT/'Source/AERISFlightControl/Terrain/AERISR034PqsLandControlHeightPathAuditObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER='AERIS32_REV3_5_R034_PQS_LANDCONTROL_HEIGHT_PATH_AUDIT_SHADOW'
PREFIX='[AERIS32 R034 LANDCONTROL HEIGHT PATH AUDIT VERIFY]'
if not SRC.is_file(): raise SystemExit(PREFIX+' missing generated observer; run apply first')
s=SRC.read_text();cs=CSPROJ.read_text();v=VERSION.read_text();bad=[]
def check(ok,label):
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: bad.append(label)
check(MARKER in s,'marker')
check('new string[] { "Minmus", "Ike", "Gilly", "Pol" }' in s,'fixed four-body target set')
check('AERISTerrainTileSystem.GameDataHashReady' in s and 'FlightGlobals.Bodies' in s,'runtime-world readiness gate')
check('PQSLandControl' in s and 'OnVertexBuildHeight' in s,'LandControl height-pass target')
check('GetILAsByteArray' in s and 'System.Reflection.Emit' in s and 'OpCodes.Stfld' in s,'IL write-path audit')
check('direct_vertHeight_writes' in s and 'height_named_field_writes' in s,'height write telemetry')
check('useHeightMap' in s and 'heightMap_present' in s and 'vHeightMax' in s,'height-map runtime config')
check('minimumRealHeight' in s and 'alterRealHeight' in s and 'alterApparentHeight' in s,'land-class height config')
check('[R034][LANDCTRL_CLASS]' in s and '[R034][LANDCTRL_RANGE]' in s,'land-class/range telemetry')
check('runtime_object_invocation_thread=MAIN_THREAD_ONLY' in s and 'worker_dispatch=false' in s and 'worker_invokes_runtime_object=false' in s,'main-thread-only audit contract')
check('db_write=false' in s and 'producer_switch=false' in s and 'gpu=false' in s and 'authority=PQS' in s,'authority isolation contract')
for token in ('ThreadPool','Task.Run','SaveEncodedBatch','CommitGeneratedTile','InvalidateBodyEnvironment','DeleteBody','RequestBuild','RequestRebuild','ComputeShader','AsyncGPUReadback'):
    check(token not in s,'forbidden token absent: '+token)
check('Terrain\\AERISR034PqsLandControlHeightPathAuditObserver.cs' in cs,'compiled')
check(MARKER in v,'build identity')
if bad: raise SystemExit(PREFIX+' FAIL: '+', '.join(bad))
print(PREFIX+' PASS')
print('runtime_scope=SHADOW_ONLY targets=Minmus,Ike,Gilly,Pol worker_dispatch=NO terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
