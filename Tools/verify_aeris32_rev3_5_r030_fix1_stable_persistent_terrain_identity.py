#!/usr/bin/env python3
from pathlib import Path
import hashlib
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
BRANCH = 'agent/aeris32-rev3-5-r030-preload-persistence-ptc-phase0'
MARKER = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'

BUILDER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
TILE = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
OBSERVER = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030Fix1PersistenceObserver.cs'
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

for required in (BUILDER, TILE, OBSERVER, CSPROJ, VERSION):
    if not required.is_file():
        raise SystemExit('FAIL: required Fix1 file missing: ' + str(required))

builder = BUILDER.read_text()
tile = TILE.read_text()
observer = OBSERVER.read_text()
csproj = CSPROJ.read_text()
version = VERSION.read_text()

checks = [
    (MARKER in builder, 'builder Fix1 marker'),
    (MARKER in tile, 'tile Fix1 marker'),
    (MARKER in observer, 'observer Fix1 marker'),
    ('writer.Write(5);' in builder, 'state writer is V5'),
    ('version != 4 && version != 5' in builder, 'state reader accepts V4/V5'),
    ('loadedAppliedPointSetSignature = reader.ReadString();' in builder,
        'V5 applied point signature read'),
    ('writer.Write(snapshotAppliedPointSetSignature ?? string.Empty);' in builder,
        'V5 applied point signature write'),
    ('PersistentTerrainIdentity = reader.ReadString();' in builder,
        'V5 persistent identity read'),
    ('TerrainWitnessHash = reader.ReadString();' in builder,
        'V5 witness read'),
    ('writer.Write(plan.PersistentTerrainIdentity ?? string.Empty);' in builder,
        'V5 persistent identity write'),
    ('writer.Write(plan.TerrainWitnessHash ?? string.Empty);' in builder,
        'V5 witness write'),
    ('legacyPointSignatureAdoptionPending = loadedVersion == 4;' in builder,
        'V4 point migration armed'),
    ('legacyPointSignatureCandidateSince' in builder,
        'V4 point migration stability gate'),
    ('now - legacyPointSignatureCandidateSince < 5.0f' in builder,
        'V4 point signature five-second stability gate'),
    ('event=ADOPT_V4_BASELINE' in builder,
        'V4 point signature adoption telemetry'),
    ('PersistentTerrainIdentityForBody(body)' in builder,
        'builder checks stable structural identity'),
    ('TerrainWitnessHashForBody(body)' in builder,
        'builder checks terrain witness'),
    ('SnapshotCompleteKeysForBody(plan.BodyName' in builder,
        'V4 migration requires indexed canonical payload'),
    ('SetR030Fix1CanonicalEnvironment' in builder,
        'builder registers canonical environment'),
    ('ClearR030Fix1CanonicalEnvironment' in builder,
        'builder clears canonical on mismatch'),
    ('event=V4_CANONICAL_ADOPTED' in builder,
        'V4 canonical migration telemetry'),
    ('[R030_FIX1][INVALIDATE]' in builder,
        'body-local invalidation telemetry'),
    ('r030Fix1CanonicalEnvironment.TryGetValue' in tile,
        'EnvironmentHash canonical intercept'),
    ('AERIS_R030_PERSISTENT_ID_V1' in tile,
        'persistent terrain identity V1'),
    ('AERIS_R030_WITNESS_V1' in tile,
        'terrain witness V1'),
    ('TrySampleTerrainAslShared(body' in tile,
        'witness uses authoritative terrain sample path'),
    ('points.GetLength(0)' in tile,
        'bounded fixed witness set'),
    ('AppendPqsStructuralFingerprint' in tile,
        'structural PQS graph fingerprint'),
    ('AppendPqsConfigurationFingerprint(builder, body);' in tile,
        'legacy fail-closed environment hash retained'),
    ('AERISR030Fix1PersistenceObserver.cs' in csproj,
        'Fix1 observer compiled'),
    ('R030 FIX1 STABLE PERSISTENT TERRAIN IDENTITY' in version,
        'build display identity'),
    ('[R030_FIX1][SUMMARY]' in observer,
        'Fix1 runtime summary telemetry'),
    ('version != 4 && version != 5' in observer,
        'Fix1 observer understands V4/V5'),
]
failed = [label for ok, label in checks if not ok]
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
if failed:
    raise SystemExit('FAIL: ' + ', '.join(failed))

# R029 accepted authorities outside the intended persistence surface must stay byte-identical.
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

# Fix1 is deliberately persistence-only. DB payload format, PQS producer, ND renderer
# and GPU presentation paths are not part of this patch.
for forbidden in (
    'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
    'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs',
):
    changed = output(['git', 'diff', '--name-only',
        'e1dfa71eb74152ef6c839c328d3e57eef5993de9', '--', forbidden])
    if changed:
        raise SystemExit('FAIL: forbidden persistence-external source changed: ' + changed)

subprocess.run(['git', 'diff', '--check'], cwd=str(ROOT), check=True)

print('PASS: AERIS32 R030 Fix1 source verification')
print('runtime_scope=PRELOAD_PERSISTENCE_ONLY')
print('state_format=V5_BACKWARD_READ_V4')
print('canonical_reuse=STABLE_IDENTITY_AND_12_POINT_WITNESS')
print('legacy_v4_migration=READY_PLUS_INDEXED_PAYLOAD_ONLY')
print('point_signature=V5_PERSISTED_WITH_V4_STABLE_BASELINE_ADOPTION')
print('builder_sha256=' + sha256(BUILDER))
print('tile_sha256=' + sha256(TILE))
print('observer_sha256=' + sha256(OBSERVER))
