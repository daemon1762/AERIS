#!/usr/bin/env python3
from pathlib import Path
import hashlib
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
MARKER = 'AERIS32_REV3_5_R031_PTC_CPU_RECONSTRUCTION_FEASIBILITY_SHADOW'
SOURCE = ROOT / 'Source/AERISFlightControl/Terrain/AERISR031PtcCpuReconstructionFeasibilityObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
branch = subprocess.check_output(['git','branch','--show-current'], cwd=str(ROOT), text=True).strip()
if branch != BRANCH:
    raise SystemExit('FAIL wrong branch ' + branch)
for p in (SOURCE, CSPROJ, VERSION):
    if not p.is_file(): raise SystemExit('FAIL missing ' + str(p))
text = SOURCE.read_text(); csproj = CSPROJ.read_text(); version = VERSION.read_text()
head = subprocess.check_output(['git','rev-parse','HEAD'], cwd=str(ROOT), text=True).strip()
h = hashlib.sha256()
files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs')) + [CSPROJ]
for path in files:
    if path == VERSION: continue
    h.update(str(path.relative_to(ROOT)).encode()); h.update(b'\0')
    h.update(path.read_bytes()); h.update(b'\0')
tree = h.hexdigest()
checks = [
    (MARKER in text, 'marker'),
    ('[R031][PTC_CPU_MOD]' in text, 'mod telemetry'),
    ('[R031][PTC_CPU_BODY]' in text, 'body telemetry'),
    ('[R031][PTC_CPU] event=FEASIBILITY_COMPLETE' in text, 'summary telemetry'),
    ('HYBRID_MAP_PLUS_PROCEDURAL' in text, 'hybrid profile'),
    ('PURE_PROCEDURAL' in text, 'procedural profile'),
    ('HYBRID_LOCAL_COMPLEX' in text, 'local modifier profile'),
    ('OnVertexBuildHeight' in text, 'height entrypoint probe'),
    ('DescribeObjectGraph' in text and 'lower.Contains("mapso")' in text and
        'lower.Contains("libnoise")' in text,
        'runtime source object inspection'),
    ('certification=NO_SHADOW_ONLY' in text, 'no certification claim'),
    ('db_write=false' in text, 'DB write disabled'),
    ('producer_switch=false' in text, 'producer switch disabled'),
    ('authority=PQS' in text, 'PQS authority'),
    ('AERISR031PtcCpuReconstructionFeasibilityObserver.cs' in csproj, 'compiled'),
    ('R031 PTC CPU RECONSTRUCTION FEASIBILITY SHADOW' in version, 'build identity'),
    ('internal const string SourceGitSha = "' + head + '";' in version,
        'build identity git head synchronized'),
    ('internal const string SourceTreeSha256 = "' + tree + '";' in version,
        'build identity source tree synchronized'),
]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed: raise SystemExit('FAIL: ' + ', '.join(failed))
for forbidden in ('SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(',
    'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'ComputeShader', 'AsyncGPUReadback'):
    if forbidden in text: raise SystemExit('FAIL forbidden token ' + forbidden)
subprocess.run(['git','diff','--check'], cwd=str(ROOT), check=True)
print('PASS: R031 CPU reconstruction feasibility shadow verification')
print('runtime_scope=OBSERVATION_ONLY')
print('terrain_authority=PQS db_write=NO producer_switch=NO gpu=NO certification=NO')
print('source_git_sha=' + head)
print('source_tree_sha256=' + tree)
