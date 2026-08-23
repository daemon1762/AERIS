#!/usr/bin/env python3
from pathlib import Path
import hashlib
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R030 VERIFY]'
MARKER = 'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER'
OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'

# Runtime-accepted R029 authority files. R030 Phase0 is observation-only and must not alter them.
R029_SHA256 = {
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs':
        'ff5d8f25b4121679246b582c03fa1d88d3d0fe7872c0b58582988bf09aa3d0f7',
    'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs':
        '286816c244b18955932bf7e05110c0cf5c5dd40a7458a966cc0f56090306dad7',
    'Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs':
        '06385f7401e124d97a094fde0d427cff713e5fb31611d286061d9bdf7e964abf',
    'Source/AERISFlightControl/Autopilot/AERISAutoTakeoffDirector.cs':
        'b76adbc33d6699804fec68c770a7f4e2e0bd744790b42ff2fbb51f2d36ebf0de',
}


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def fail(message):
    raise SystemExit(PREFIX + ' FAIL: ' + message)

if not OBSERVER.is_file():
    fail('observer source missing; run R030 applicator first')
text = OBSERVER.read_text()
csproj = CSPROJ.read_text()
version = VERSION.read_text()

required = (
    MARKER,
    '[KSPAddon(KSPAddon.Startup.MainMenu, false)]',
    '[R030][PRELOAD_PERSIST]',
    '[R030][PTC_BODY]',
    '[R030][PTC0]',
    'AERISTerrainTileSystem.GameDataHashReady',
    'AERISTerrainTileSystem.EnvironmentHashForBody(body)',
    'AERISTerrainPreloadFormat.StateMagic',
    'version != 4',
    'CompletedEnvironmentHash',
    'CompletedCoastlineEnvironmentHash',
    'structural_hash=',
    'primitive_hash=',
    'planet_hash=',
    'state_ready=',
    'env_match=',
    'completed_env_match=',
    'classification=',
)
for token in required:
    if token not in text:
        fail('observer contract missing: ' + token)

include = '<Compile Include="Terrain\\AERISR030PreloadPersistencePtcPhase0Observer.cs" />'
if csproj.count(include) != 1:
    fail('csproj observer include count=' + str(csproj.count(include)))

for token in (
    'AERIS32 REV3.5 R030 PRELOAD PERSISTENCE + PTC PHASE0 OBSERVER',
    'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0',
):
    if token not in version:
        fail('R030 identity missing: ' + token)

# Phase0 must remain observer-only. These mutating/sampling owners are forbidden in the new observer.
for token in (
    'new AERISTerrainPreloadDatabase',
    'AERISTerrainBlockPipeline',
    'RequestBuild(',
    'RequestRebuild(',
    'RequestDelete(',
    'CommitGeneratedTile(',
    'TryCommit',
    'File.Delete(',
    'Directory.Delete(',
    'PQS.GetSurfaceHeight',
    'GetSurfaceHeight(',
):
    if token in text:
        fail('observer contains forbidden runtime mutation/sampling token: ' + token)

for relative, expected in R029_SHA256.items():
    path = ROOT / relative
    if not path.is_file():
        fail('R029 authority file missing: ' + relative)
    actual = sha256(path)
    if actual != expected:
        fail('R029 authority changed: ' + relative + ' sha=' + actual)

# Only the observer, csproj compile registration and generated build identity may be materialized.
changed = subprocess.check_output(
    ['git', 'diff', '--name-only', 'HEAD', '--'], cwd=str(ROOT), text=True).splitlines()
allowed = {
    'Source/AERISFlightControl/AERISFlightControl.csproj',
    'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs',
}
for path in changed:
    if path not in allowed:
        fail('unexpected tracked working-tree change: ' + path)

subprocess.run(['git', 'diff', '--check'], cwd=str(ROOT), check=True)
print('PASS: AERIS32 R030 Phase0 source verification')
print('PASS: observer-only runtime contract')
print('PASS: R029 runtime-accepted authority files byte-for-byte preserved')
print('PASS: preload DB/producer/display/control paths untouched')
print('observer_sha256=' + sha256(OBSERVER))
