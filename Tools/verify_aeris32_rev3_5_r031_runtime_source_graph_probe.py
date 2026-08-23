#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
SOURCE = SRC / 'AERISR031PtcRuntimeSourceGraphObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER = 'AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW'

branch = subprocess.check_output(['git','branch','--show-current'], cwd=str(ROOT), text=True).strip()
if branch != BRANCH:
    raise SystemExit('FAIL: wrong branch ' + branch + ' expected=' + BRANCH)
for path in (SOURCE, CSPROJ, VERSION):
    if not path.is_file(): raise SystemExit('FAIL: missing ' + str(path))
text = SOURCE.read_text(); csproj = CSPROJ.read_text(); version = VERSION.read_text()
checks = [
    (MARKER in text, 'runtime source graph marker'),
    ('[KSPAddon(KSPAddon.Startup.MainMenu, false)]' in text, 'MainMenu observer'),
    ('[R031][PTC_GRAPH_MOD]' in text, 'per-mod telemetry'),
    ('[R031][PTC_GRAPH_BODY]' in text, 'per-body telemetry'),
    ('[R031][PTC_GRAPH] event=COMPLETE' in text, 'summary telemetry'),
    ('likely_height_contributor=' in text, 'height contributor diagnostic'),
    ('source_objects=' in text, 'runtime source object diagnostic'),
    ('shadow_profile=' in text, 'body complexity profile'),
    ('certification=NO_SHADOW_ONLY' in text, 'no certification claim'),
    ('db_write=false' in text, 'DB write disabled'),
    ('producer_switch=false' in text, 'producer switch disabled'),
    ('authority=PQS' in text, 'PQS authority'),
    ('AERISR031PtcRuntimeSourceGraphObserver.cs' in csproj, 'source compiled'),
    (MARKER in version, 'build identity marker'),
]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed: raise SystemExit('FAIL: ' + ', '.join(failed))
for forbidden in (
    'SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(',
    'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'TrySampleTerrainAslShared(',
    'SetR030Fix1CanonicalEnvironment(', 'new AERISTerrainPreloadDatabase',
    'ComputeShader', 'GraphicsBuffer', 'AsyncGPUReadback'):
    if forbidden in text:
        raise SystemExit('FAIL: forbidden runtime source graph token ' + forbidden)
subprocess.run(['git','diff','--check'], cwd=str(ROOT), check=True)
print('PASS: R031 runtime PQS source graph shadow verification')
print('runtime_scope=OBSERVATION_ONLY')
print('terrain_db_mutation=NO producer_switch=NO certification=NO authority=PQS')
