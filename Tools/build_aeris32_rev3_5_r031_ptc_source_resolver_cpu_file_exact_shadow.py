#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R031 PTC SHADOW BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
R030_FIX1 = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'
R030_FIX2 = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'
R031 = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def tree_stats(path):
    if not path.is_dir(): return (False, 0, 0)
    files = [p for p in path.rglob('*') if p.is_file()]
    total = 0
    for p in files:
        try: total += p.stat().st_size
        except OSError: pass
    return (True, len(files), total)


def marker_in_bytes(data, text):
    return text.encode() in data or text.encode('utf-16le') in data


def verify_r030_parent_contract(builder, tile, phase0, fix1obs):
    # The original R030 verifiers are intentionally branch-scoped. R031 is a direct
    # descendant branch, so re-running them would fail only because the branch name is
    # different. Verify the inherited accepted contract here instead, then freeze the
    # complete persistence/DB/renderer surface byte-for-byte before R031 materializes.
    for path in (builder, tile, phase0, fix1obs):
        if not path.is_file():
            raise SystemExit(PREFIX + ' inherited R030 file missing: ' + str(path))

    builder_text = builder.read_text()
    tile_text = tile.read_text()
    phase0_text = phase0.read_text()
    fix1_text = fix1obs.read_text()
    checks = (
        (R030_FIX1 in builder_text, 'R030 Fix1 builder marker'),
        (R030_FIX2 in builder_text, 'R030 Fix2 builder marker'),
        (R030_FIX1 in tile_text, 'R030 Fix1 tile marker'),
        (R030_FIX2 in phase0_text, 'R030 Fix2 Phase0 marker'),
        (R030_FIX1 in fix1_text, 'R030 Fix1 observer marker'),
        ('writer.Write(5);' in builder_text, 'R030 V5 state writer'),
        ('version != 4 && version != 5' in builder_text, 'R030 V4/V5 state reader'),
        ('PersistentTerrainIdentityForBody(body)' in builder_text,
            'R030 stable terrain identity validation'),
        ('TerrainWitnessHashForBody(body)' in builder_text,
            'R030 terrain witness validation'),
        ('SetR030Fix1CanonicalEnvironment' in builder_text,
            'R030 canonical environment reuse'),
        ('r030Fix2MigrationLogged.Add(body.name)' in builder_text,
            'R030 one-shot migration telemetry'),
        ('AERIS_R030_WITNESS_V1' in tile_text, 'R030 witness authority marker'),
        ('diagnostic_planet_hash=' in phase0_text and
            'hash_authority=DIAGNOSTIC_ONLY' in phase0_text,
            'R030 diagnostic hash non-authority'),
        ('[R030_FIX1][SUMMARY]' in fix1_text, 'R030 persistence summary telemetry'),
    )
    failed = []
    for ok, label in checks:
        print(PREFIX + (' PASS ' if ok else ' FAIL ') + label)
        if not ok: failed.append(label)
    if failed:
        raise SystemExit(PREFIX + ' inherited R030 contract FAIL: ' + ', '.join(failed))

    # R029 accepted authorities outside the R030 persistence surface stay exact.
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
        if not path.is_file():
            raise SystemExit(PREFIX + ' accepted authority missing: ' + relative)
        actual = sha256(path)
        if actual != expected:
            raise SystemExit(PREFIX + ' accepted authority changed: ' + relative +
                ' sha256=' + actual)
        print(PREFIX + ' PASS accepted authority ' + relative)


parser = argparse.ArgumentParser(description='R031 PTC source resolver + CPU FILE_EXACT shadow build. Preserves R030 V5 DB exactly.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))
branch = output(['git','branch','--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

builder = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
tile = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
phase0 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
fix1obs = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030Fix1PersistenceObserver.cs'

# Materialize the accepted R030 persistence chain only when absent. Do not invoke the
# original R030 verifiers here: they deliberately reject any branch other than R030.
if not builder.is_file() or R030_FIX1 not in builder.read_text():
    if not phase0.is_file():
        run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix1_stable_persistent_terrain_identity.py'])
if R030_FIX2 not in builder.read_text():
    run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix2_persistence_telemetry_cleanup.py'])
# Parent build identity hotfix is harmless and keeps intermediate identity coherent.
run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix2_hotfix1_build_identity_sync.py'])
verify_r030_parent_contract(builder, tile, phase0, fix1obs)

# Freeze accepted R030 persistence/DB/renderer sources across R031 materialization.
frozen = [
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
]
pre_r031 = {str(p): sha256(p) for p in frozen}

run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r031_ptc_source_resolver_cpu_file_exact_shadow.py'])
run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r031_hotfix1_normalize_hint_compile.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r031_ptc_source_resolver_cpu_file_exact_shadow.py'])
for p in frozen:
    actual = sha256(p)
    if actual != pre_r031[str(p)]:
        raise SystemExit(PREFIX + ' R031 altered frozen R030 source: ' + str(p))
print(PREFIX + ' PASS frozen R030 persistence/DB/renderer sources unchanged')

src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
run(['xbuild','/p:Configuration=Release','/p:KSPDIR=' + str(ksp), csproj])
built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file(): raise SystemExit(PREFIX + ' built DLL missing')

repo_mod = ROOT / 'GameData/AERISFlightControl'
repo_plugins = repo_mod / 'Plugins'
repo_plugins.mkdir(parents=True, exist_ok=True)
repo_dll = repo_plugins / 'AERISFlightControl.dll'
shutil.copy2(str(built), str(repo_dll))

repo_shaders = repo_mod / 'Shaders'
historical_shaders = ROOT.parent / 'AERIS' / 'GameData/AERISFlightControl/Shaders'
if repo_shaders.is_dir():
    shader_source = repo_shaders; shader_provenance = 'R031_REPOSITORY'
elif historical_shaders.is_dir():
    shader_source = historical_shaders; shader_provenance = 'R029_HISTORICAL_WORKTREE'
else:
    raise SystemExit(PREFIX + ' shader bundle missing; refusing incomplete install')
shader_files = [p for p in shader_source.rglob('*') if p.is_file()]
if not shader_files: raise SystemExit(PREFIX + ' shader source empty')

installed_mod = ksp / 'GameData/AERISFlightControl'
plugin_data = installed_mod / 'PluginData'
before_exists, before_files, before_bytes = tree_stats(plugin_data)
settings_path = installed_mod / 'Config/AERISSettings.cfg'
settings_backup = settings_path.read_bytes() if settings_path.is_file() else None

installed_mod.parent.mkdir(parents=True, exist_ok=True)
shutil.copytree(str(repo_mod), str(installed_mod), dirs_exist_ok=True)
shutil.copytree(str(shader_source), str(installed_mod / 'Shaders'), dirs_exist_ok=True)
if settings_backup is not None:
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_bytes(settings_backup)
after_exists, after_files, after_bytes = tree_stats(plugin_data)
if before_exists and (not after_exists or before_files != after_files or before_bytes != after_bytes):
    raise SystemExit(PREFIX + ' PluginData changed during overlay: before=' +
        str((before_files,before_bytes)) + ' after=' + str((after_files,after_bytes)))

installed_dll = installed_mod / 'Plugins/AERISFlightControl.dll'
repo_sha = sha256(repo_dll); installed_sha = sha256(installed_dll)
if repo_sha != installed_sha: raise SystemExit(PREFIX + ' DLL SHA mismatch')
dll = installed_dll.read_bytes()
for token, label in ((R031,'R031 marker'),('[R031][PTC_RESOLVE]','resolver telemetry'),
    ('certification=NO_SHADOW_ONLY','shadow certification guard'),('authority=PQS','PQS authority')):
    if not marker_in_bytes(dll, token):
        raise SystemExit(PREFIX + ' installed DLL missing ' + label)

head = output(['git','rev-parse','HEAD'])
identity = (
    'aeris_candidate=' + R031 + '\n'
    'branch=' + BRANCH + '\n'
    'source_head=' + head + '\n'
    'parent=AERIS32_R030_FIX2_HOTFIX1_RUNTIME_ACCEPTED\n'
    'variant=' + R031 + '\n'
    'runtime_change=PTC_SHADOW_OBSERVER_ONLY\n'
    'terrain_db_write=NO\n'
    'producer_switch=NO\n'
    'terrain_authority=PQS\n'
    'gpu=NO\n'
    'cpu_decoders=PGM_P2_P5_RAW16_LE_SQUARE\n'
    'certification=NO_SHADOW_ONLY\n'
    'install_mode=OVERLAY_PRESERVE_PLUGINDATA_AND_USER_SETTINGS\n'
    'plugin_data_before_files=' + str(before_files) + '\n'
    'plugin_data_before_bytes=' + str(before_bytes) + '\n'
    'plugin_data_after_files=' + str(after_files) + '\n'
    'plugin_data_after_bytes=' + str(after_bytes) + '\n'
    'shader_provenance=' + shader_provenance + '\n'
    'shader_file_count=' + str(len(shader_files)) + '\n'
    'dll_sha256=' + installed_sha + '\n')
(repo_mod / 'AERISCandidateBuildIdentity.txt').write_text(identity)
(installed_mod / 'AERISCandidateBuildIdentity.txt').write_text(identity)

print(PREFIX + ' PASS')
print('branch=' + branch)
print('head=' + head)
print('runtime_change=PTC_SHADOW_OBSERVER_ONLY')
print('terrain_db_write=NO producer_switch=NO terrain_authority=PQS gpu=NO')
print('plugin_data_preserved=' + str(before_exists))
print('plugin_data_files=' + str(after_files))
print('plugin_data_bytes=' + str(after_bytes))
print('shader_provenance=' + shader_provenance)
print('dll_sha256=' + installed_sha)
print('TEST: fully restart KSP to Main Menu; wait 15-30 s for [R031][PTC] SHADOW_COMPLETE; do not delete PluginData.')
