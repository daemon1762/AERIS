#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
MARKER = 'AERIS32_REV3_5_R031_PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW'
SOURCE = ROOT / 'Source/AERISFlightControl/Terrain/AERISR031PtcWorkerSnapshotFeasibilityObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'

branch = subprocess.check_output(['git','branch','--show-current'], cwd=str(ROOT), text=True).strip()
if branch != BRANCH:
    raise SystemExit('FAIL wrong branch ' + branch)
for p in (SOURCE, CSPROJ, VERSION):
    if not p.is_file(): raise SystemExit('FAIL missing ' + str(p))
text = SOURCE.read_text(); csproj = CSPROJ.read_text(); version = VERSION.read_text()
checks = [
    (MARKER in text, 'marker'),
    ('[R031][PTC_SNAPSHOT_MOD]' in text, 'per-mod telemetry'),
    ('[R031][PTC_SNAPSHOT_BODY]' in text, 'per-body telemetry'),
    ('[R031][PTC_SNAPSHOT] event=FEASIBILITY_COMPLETE' in text, 'summary telemetry'),
    ('MANAGED_MATH_SNAPSHOT_CANDIDATE' in text, 'managed math candidate'),
    ('MAPSO_MAIN_THREAD_COPY_CANDIDATE' in text, 'MapSO copy candidate'),
    ('CUSTOM_ADAPTER_REQUIRED' in text, 'custom adapter classification'),
    ('PURE_DATA_WORKER_CANDIDATE' in text, 'pure data body classification'),
    ('worker_invokes_runtime_object=false' in text, 'worker runtime object invocation forbidden'),
    ('PrimitiveSize' in text and 'primitive_array_bytes=' in text, 'primitive array sizing'),
    ('FindThreeDoubleMethod' in text and 'GetValue' in text, 'managed math method introspection'),
    ('IsUnityBacked' in text, 'Unity backed object detection'),
    ('certification=NO_SHADOW_ONLY' in text, 'no certification claim'),
    ('db_write=false' in text, 'DB write disabled'),
    ('producer_switch=false' in text, 'producer switch disabled'),
    ('authority=PQS' in text, 'PQS authority'),
    ('AERISR031PtcWorkerSnapshotFeasibilityObserver.cs' in csproj, 'compiled'),
    ('R031 PTC WORKER SNAPSHOT FEASIBILITY SHADOW' in version, 'build identity'),
]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed: raise SystemExit('FAIL: ' + ', '.join(failed))
for forbidden in ('SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(',
    'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'ComputeShader', 'AsyncGPUReadback',
    'Task.Run(', 'ThreadPool.QueueUserWorkItem'):
    if forbidden in text: raise SystemExit('FAIL forbidden token ' + forbidden)
subprocess.run(['git','diff','--check'], cwd=str(ROOT), check=True)
print('PASS: R031 worker snapshot feasibility shadow verification')
print('runtime_scope=OBSERVATION_ONLY')
print('worker_invokes_runtime_object=NO')
print('terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
