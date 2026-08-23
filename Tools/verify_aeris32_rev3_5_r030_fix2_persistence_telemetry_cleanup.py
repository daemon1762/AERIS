#!/usr/bin/env python3
from pathlib import Path
import hashlib
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
BRANCH = 'agent/aeris32-rev3-5-r030-preload-persistence-ptc-phase0'
FIX1 = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'
MARKER = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'

BUILDER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
TILE = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
BASE_OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
FIX1_OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030Fix1PersistenceObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT),
        text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit('FAIL: wrong branch: ' + branch + ' expected=' + BRANCH)

for required in (BUILDER, TILE, BASE_OBSERVER, FIX1_OBSERVER, CSPROJ, VERSION):
    if not required.is_file():
        raise SystemExit('FAIL: required Fix2 file missing: ' + str(required))

builder = BUILDER.read_text()
tile = TILE.read_text()
base = BASE_OBSERVER.read_text()
fix1_observer = FIX1_OBSERVER.read_text()
version = VERSION.read_text()

checks = [
    (FIX1 in tile, 'Fix1 terrain identity authority preserved'),
    (FIX1 in fix1_observer, 'Fix1 persistence observer preserved'),
    (MARKER in builder, 'builder Fix2 marker'),
    (MARKER in base, 'phase0 observer Fix2 marker'),
    ('r030Fix2MigrationLogged' in builder, 'migration one-shot set'),
    ('r030Fix2MigrationLogged.Add(body.name)' in builder,
        'migration one-shot gate'),
    ('log_policy=ONE_SHOT_PER_BODY' in builder,
        'migration one-shot telemetry label'),
    ('writer.Write(5);' in builder, 'V5 state writer preserved'),
    ('version != 4 && version != 5' in builder,
        'builder V4/V5 reader preserved'),
    ('PersistentTerrainIdentityForBody(body)' in builder,
        'stable identity validation preserved'),
    ('TerrainWitnessHashForBody(body)' in builder,
        'witness validation preserved'),
    ('SetR030Fix1CanonicalEnvironment' in builder,
        'canonical reuse preserved'),
    ('[R030_FIX1][INVALIDATE]' in builder,
        'body-local invalidation telemetry preserved'),
    ('out int version, out string pointSignature, out string status' in base,
        'phase0 reader reports state version'),
    ('version != 4 && version != 5' in base,
        'phase0 observer accepts V4/V5'),
    ('if (version >= 5) pointSignature = reader.ReadString();' in base,
        'phase0 observer reads V5 point signature'),
    ('reader.ReadString(); // persistent terrain identity' in base,
        'phase0 observer consumes V5 identity field'),
    ('reader.ReadString(); // terrain witness' in base,
        'phase0 observer consumes V5 witness field'),
    ('state_version=' in base, 'phase0 state version telemetry'),
    ('applied_point_signature=' in base,
        'phase0 point-signature telemetry'),
    ('diagnostic_planet_hash=' in base,
        'runtime hash explicitly diagnostic'),
    ('hash_authority=DIAGNOSTIC_ONLY' in base,
        'runtime hash authority label'),
    ('R030 FIX2 PERSISTENCE TELEMETRY CLEANUP' in version,
        'Fix2 build display identity'),
]
failed = [label for ok, label in checks if not ok]
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
if failed:
    raise SystemExit('FAIL: ' + ', '.join(failed))

# Exactly one logging call for V4_CANONICAL_ADOPTED remains in source. The repeated
# runtime lines were caused by the refresh loop, not by multiple logging sites.
count = builder.count('event=V4_CANONICAL_ADOPTED')
print(('[PASS] ' if count == 1 else '[FAIL] ') +
    'V4 migration logging site count=' + str(count))
if count != 1:
    raise SystemExit('FAIL: unexpected V4 migration logging site count')

# R029 runtime-accepted authorities outside the persistence/telemetry surface remain exact.
frozen_hashes = {
    'Source/AERISFlightControl/Autopilot/AERISAutoTakeoffDirector.cs':
        'b76adbc33d6699804fec68c770a7f4e2e0bd744790b42ff2fbb51f2d36ebf0de',
    'Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs':
        '06385f7401e124d97a094fde0d427cff713e5fb31611d286061d9bdf7e964abf',
    'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs':
        '286816c244b18955932bf7e05110c0cf5c5dd40a7458a966cc0f56090306dad7',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs':
        'ff5d8f25b4121679246b582c03fa1d88d3d0fe7872c0b58582988bf09aa3d0f7',
}
for relative, expected in frozen_hashes.items():
    path = ROOT / relative
    actual = sha256(path)
    ok = actual == expected
    print(('[PASS] ' if ok else '[FAIL] ') +
        'R029 authority preserved ' + relative + ' sha256=' + actual)
    if not ok:
        raise SystemExit('FAIL: R029 authority changed: ' + relative)

# Fix2 is telemetry-only on top of Fix1. It must not touch DB payload, producer,
# codec, renderer, rasterizer or control paths.
for forbidden in (
    'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect',
    'Source/AERISFlightControl/Landing',
):
    changed = output(['git', 'diff', '--name-only',
        'e1dfa71eb74152ef6c839c328d3e57eef5993de9', '--', forbidden])
    if changed:
        raise SystemExit('FAIL: forbidden Fix2 source changed: ' + changed)

subprocess.run(['git', 'diff', '--check'], cwd=str(ROOT), check=True)

print('PASS: AERIS32 R030 Fix2 source verification')
print('runtime_scope=FIX1_PERSISTENCE_UNCHANGED_TELEMETRY_CLEANUP_ONLY')
print('migration_log=ONE_SHOT_PER_BODY_PER_PROCESS')
print('phase0_state_reader=V4_V5')
print('phase0_runtime_hash=DIAGNOSTIC_ONLY')
print('builder_sha256=' + sha256(BUILDER))
print('phase0_observer_sha256=' + sha256(BASE_OBSERVER))
print('fix1_observer_sha256=' + sha256(FIX1_OBSERVER))
