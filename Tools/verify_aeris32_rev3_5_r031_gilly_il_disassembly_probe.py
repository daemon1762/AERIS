#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
BRANCH='agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
MARKER='AERIS32_REV3_5_R031_PTC_GILLY_IL_DISASSEMBLY_SHADOW'
SOURCE=ROOT/'Source/AERISFlightControl/Terrain/AERISR031PtcGillyIlDisassemblyObserver.cs'
CSPROJ=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
branch=subprocess.check_output(['git','branch','--show-current'],cwd=str(ROOT),text=True).strip()
if branch!=BRANCH: raise SystemExit('FAIL wrong branch '+branch)
for p in (SOURCE,CSPROJ,VERSION):
    if not p.is_file(): raise SystemExit('FAIL missing '+str(p))
text=SOURCE.read_text(); csproj=CSPROJ.read_text(); version=VERSION.read_text()
checks=[
 (MARKER in text,'marker'),
 ('[R031][PTC_GILLY_IL_METHOD]' in text,'method telemetry'),
 ('[R031][PTC_GILLY_IL_CHUNK]' in text,'chunk telemetry'),
 ('[R031][PTC_GILLY_IL] event=DISASSEMBLY_COMPLETE' in text,'summary telemetry'),
 ('GetILAsByteArray' in text,'IL extraction'),
 ('ResolveMember' in text and 'ResolveString' in text,'metadata token resolution'),
 ('OperandType.InlineMethod' in text and 'OperandType.InlineField' in text,'member operand decoding'),
 ('OnVertexBuildHeight' in text and 'CalculateSpectralWeights' in text,'target methods'),
 ('worker_invokes_runtime_object=false' in text,'worker runtime invocation forbidden'),
 ('authority=PQS' in text,'PQS authority'),
 ('AERISR031PtcGillyIlDisassemblyObserver.cs' in csproj,'compiled'),
 ('R031 PTC GILLY IL DISASSEMBLY SHADOW' in version,'build identity')]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: failed.append(label)
if failed: raise SystemExit('FAIL: '+', '.join(failed))
for forbidden in ('SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(',
 'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'ComputeShader', 'AsyncGPUReadback',
 'Task.Run(', 'ThreadPool.QueueUserWorkItem'):
    if forbidden in text: raise SystemExit('FAIL forbidden token '+forbidden)
subprocess.run(['git','diff','--check'],cwd=str(ROOT),check=True)
print('PASS: R031 Gilly IL disassembly shadow verification')
print('runtime_scope=OBSERVATION_ONLY')
print('terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')