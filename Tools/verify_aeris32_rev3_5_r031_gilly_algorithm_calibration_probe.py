#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
MARKER = 'AERIS32_REV3_5_R031_PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW'
SOURCE = ROOT / 'Source/AERISFlightControl/Terrain/AERISR031PtcGillyAlgorithmCalibrationObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'

branch = subprocess.check_output(['git','branch','--show-current'], cwd=str(ROOT), text=True).strip()
if branch != BRANCH: raise SystemExit('FAIL wrong branch ' + branch)
for p in (SOURCE, CSPROJ, VERSION):
    if not p.is_file(): raise SystemExit('FAIL missing ' + str(p))
text = SOURCE.read_text(); csproj = CSPROJ.read_text(); version = VERSION.read_text()
checks = [
    (MARKER in text, 'marker'),
    ('body=Gilly' in text, 'Gilly-only target'),
    ('[R031][PTC_GILLY_MOD]' in text, 'mod telemetry'),
    ('[R031][PTC_GILLY_MATH]' in text, 'math-object telemetry'),
    ('[R031][PTC_GILLY_ALGO] event=CALIBRATION_COMPLETE' in text, 'summary telemetry'),
    ('CalibrationPoints' in text and 'Calibrate(' in text, 'fixed-input calibration'),
    ('DescribeFields' in text and 'static_fields=' in text, 'instance/static field capture'),
    ('DescribeMethods' in text and 'methods=' in text, 'method signature capture'),
    ('runtime_object_invocation_thread=MAIN_THREAD_ONLY' in text, 'runtime invocation main-thread only'),
    ('worker_invokes_runtime_object=false' in text, 'worker runtime object invocation forbidden'),
    ('certification=NO_SHADOW_ONLY' in text, 'no certification claim'),
    ('db_write=false' in text, 'DB write disabled'),
    ('producer_switch=false' in text, 'producer switch disabled'),
    ('authority=PQS' in text, 'PQS authority'),
    ('AERISR031PtcGillyAlgorithmCalibrationObserver.cs' in csproj, 'compiled'),
    ('R031 PTC GILLY ALGORITHM CALIBRATION SHADOW' in version, 'build identity'),
]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed: raise SystemExit('FAIL: ' + ', '.join(failed))
for forbidden in ('SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(',
    'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'ComputeShader', 'AsyncGPUReadback',
    'Task.Run(', 'ThreadPool.QueueUserWorkItem', 'SubmitLatest(', 'SubmitRequired('):
    if forbidden in text: raise SystemExit('FAIL forbidden token ' + forbidden)
subprocess.run(['git','diff','--check'], cwd=str(ROOT), check=True)
print('PASS: R031 Gilly algorithm calibration shadow verification')
print('runtime_scope=MAIN_THREAD_CALIBRATION_ONLY')
print('worker_invokes_runtime_object=NO')
print('terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
