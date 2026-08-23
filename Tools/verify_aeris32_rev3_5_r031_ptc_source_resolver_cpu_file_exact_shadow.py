#!/usr/bin/env python3
from pathlib import Path
import hashlib
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
MARKER = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
RESOLVER = SRC / 'AERISPtcSourceResolver.cs'
COMPILER = SRC / 'AERISPtcCpuFileExact.cs'
OBSERVER = SRC / 'AERISR031PtcShadowObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

branch = subprocess.check_output(['git','branch','--show-current'], cwd=str(ROOT), text=True).strip()
if branch != BRANCH:
    raise SystemExit('FAIL: wrong branch ' + branch + ' expected=' + BRANCH)
for path in (RESOLVER, COMPILER, OBSERVER, CSPROJ, VERSION):
    if not path.is_file():
        raise SystemExit('FAIL: missing R031 file ' + str(path))
resolver = RESOLVER.read_text()
compiler = COMPILER.read_text()
observer = OBSERVER.read_text()
csproj = CSPROJ.read_text()
version = VERSION.read_text()

checks = [
    (MARKER in resolver, 'resolver marker'),
    (MARKER in compiler, 'CPU compiler marker'),
    (MARKER in observer, 'shadow observer marker'),
    ('FileExactCandidate' in resolver, 'FILE_EXACT candidate classification'),
    ('NO_UNIQUE_RUNTIME_FILE_SOURCE' in resolver, 'unknown fail-closed classification'),
    ('AMBIGUOUS_RUNTIME_FILE_SOURCE' in resolver, 'ambiguous source fail-closed'),
    ('SOURCE_FOUND_DECODER_NOT_YET_SUPPORTED' in resolver, 'unsupported decoder fail-closed'),
    ('Directory.GetFiles(gameDataRoot, "*", SearchOption.AllDirectories)' in resolver,
        'GameData source index worker path'),
    ('TryDecodePgm' in compiler, 'PGM CPU decoder'),
    ('TryDecodeRaw16Square' in compiler, 'RAW16 CPU decoder'),
    ('bestMax <= 2.0 && bestRms <= 0.75' in compiler, 'shadow witness threshold'),
    ('certification=NO_SHADOW_ONLY' in observer, 'no certification claim'),
    ('db_write=false' in observer, 'DB write explicitly disabled'),
    ('producer_switch=false' in observer, 'producer switch explicitly disabled'),
    ('gpu=false' in observer, 'GPU explicitly disabled'),
    ('authority=PQS' in observer, 'PQS remains authority'),
    ('SubmitLatest(' in observer and 'AERISRuntimeLane.GeneralCompute' in observer,
        'source IO/CPU work scheduled on worker runtime'),
    ('TrySampleTerrainAslShared(body' in observer, 'PQS witness capture'),
    ("Trim('\\\"').Replace('\\\\', '/')" in resolver, 'NormalizeHint compile hotfix'),
    ('AERISPtcSourceResolver.cs' in csproj and 'AERISPtcCpuFileExact.cs' in csproj and
        'AERISR031PtcShadowObserver.cs' in csproj, 'R031 sources compiled'),
    ('R031 PTC SOURCE RESOLVER + CPU FILE_EXACT SHADOW' in version,
        'R031 build display identity'),
]
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit('FAIL: ' + ', '.join(failed))

# R031 shadow files must have no Terrain DB mutation/producer-authority calls.
combined = resolver + '\n' + compiler + '\n' + observer
for forbidden in (
    'SaveEncodedBatch(', 'CommitGeneratedTile(', 'InvalidateBodyEnvironment(',
    'DeleteBody(', 'RequestBuild(', 'RequestRebuild(', 'SetR030Fix1CanonicalEnvironment(',
    'new AERISTerrainPreloadDatabase', 'AERISTerrainTileSource.PreloadBuilderGenerated',
    'ComputeShader', 'GraphicsBuffer', 'AsyncGPUReadback'):
    if forbidden in combined:
        raise SystemExit('FAIL: forbidden R031 shadow mutation/authority token: ' + forbidden)

subprocess.run(['git','diff','--check'], cwd=str(ROOT), check=True)
print('PASS: AERIS32 R031 PTC source resolver + CPU FILE_EXACT shadow verification')
print('runtime_scope=SHADOW_ONLY')
print('terrain_db_mutation=NO')
print('producer_authority=PQS_ONLY')
print('gpu=NO')
print('resolver_sha256=' + sha256(RESOLVER))
print('compiler_sha256=' + sha256(COMPILER))
print('observer_sha256=' + sha256(OBSERVER))
