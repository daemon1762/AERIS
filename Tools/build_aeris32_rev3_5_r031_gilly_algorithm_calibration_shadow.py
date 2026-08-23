#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R031 GILLY CAL BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
PARENT = 'AERIS32_REV3_5_R031_PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW'
MARKER = 'AERIS32_REV3_5_R031_PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW'

def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)

def out(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

def stats(path):
    if not path.is_dir():
        return (False, 0, 0)
    files = [p for p in path.rglob('*') if p.is_file()]
    total = 0
    for p in files:
        try:
            total += p.stat().st_size
        except OSError:
            pass
    return (True, len(files), total)

def marker(data, text):
    return text.encode() in data or text.encode('utf-16le') in data

parser = argparse.ArgumentParser(description='R031 Gilly algorithm calibration shadow build/install')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path missing: ' + str(ksp))
branch = out(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch ' + branch + ' expected=' + BRANCH)

# Resume-safe parent handling. If the worker-snapshot observer is already materialized,
# validate its immutable shadow contract instead of rebuilding it and trying to roll the
# generated build identity backwards from this child stage.
parent_source = ROOT / 'Source/AERISFlightControl/Terrain/AERISR031PtcWorkerSnapshotFeasibilityObserver.cs'
csproj = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
if not parent_source.is_file() or PARENT not in parent_source.read_text():
    print(PREFIX + ' parent worker snapshot materialization absent; building parent once')
    run([sys.executable,
         ROOT / 'Tools/build_aeris32_rev3_5_r031_worker_snapshot_feasibility_shadow.py',
         str(ksp)])
else:
    ptext = parent_source.read_text()
    ctext = csproj.read_text()
    parent_checks = [
        ('[R031][PTC_SNAPSHOT_MOD]' in ptext, 'parent per-mod telemetry'),
        ('[R031][PTC_SNAPSHOT_BODY]' in ptext, 'parent per-body telemetry'),
        ('[R031][PTC_SNAPSHOT] event=FEASIBILITY_COMPLETE' in ptext, 'parent summary telemetry'),
        ('worker_invokes_runtime_object=false' in ptext, 'parent worker runtime-object guard'),
        ('authority=PQS' in ptext, 'parent PQS authority'),
        ('AERISR031PtcWorkerSnapshotFeasibilityObserver.cs' in ctext, 'parent csproj registration'),
    ]
    failed = [label for ok, label in parent_checks if not ok]
    for ok, label in parent_checks:
        print(('[PASS] ' if ok else '[FAIL] ') + label)
    if failed:
        raise SystemExit(PREFIX + ' invalid parent worker snapshot materialization: ' + ', '.join(failed))
    print(PREFIX + ' PASS existing parent worker snapshot materialization reused; parent rebuild skipped')

frozen = [
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
]
before = {str(p): sha(p) for p in frozen}
run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r031_gilly_algorithm_calibration_probe.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r031_gilly_algorithm_calibration_probe.py'])
for p in frozen:
    if sha(p) != before[str(p)]:
        raise SystemExit(PREFIX + ' frozen R030 source changed: ' + str(p))
print(PREFIX + ' PASS frozen R030 persistence/DB/renderer sources unchanged')

src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
run(['xbuild', '/p:Configuration=Release', '/p:KSPDIR=' + str(ksp), csproj])
built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' built DLL missing')

repo = ROOT / 'GameData/AERISFlightControl'
(repo / 'Plugins').mkdir(parents=True, exist_ok=True)
repo_dll = repo / 'Plugins/AERISFlightControl.dll'
shutil.copy2(str(built), str(repo_dll))

repo_shaders = repo / 'Shaders'
historical_shaders = ROOT.parent / 'AERIS' / 'GameData/AERISFlightControl/Shaders'
if repo_shaders.is_dir():
    shader_source = repo_shaders
    shader_provenance = 'R031_REPOSITORY'
elif historical_shaders.is_dir():
    shader_source = historical_shaders
    shader_provenance = 'R029_HISTORICAL_WORKTREE'
else:
    raise SystemExit(PREFIX + ' shader bundle missing')
shader_files = [p for p in shader_source.rglob('*') if p.is_file()]
if not shader_files:
    raise SystemExit(PREFIX + ' shader source empty')

installed = ksp / 'GameData/AERISFlightControl'
pdata = installed / 'PluginData'
before_exists, before_files, before_bytes = stats(pdata)
settings = installed / 'Config/AERISSettings.cfg'
settings_backup = settings.read_bytes() if settings.is_file() else None
installed.parent.mkdir(parents=True, exist_ok=True)
shutil.copytree(str(repo), str(installed), dirs_exist_ok=True)
shutil.copytree(str(shader_source), str(installed / 'Shaders'), dirs_exist_ok=True)
if settings_backup is not None:
    settings.parent.mkdir(parents=True, exist_ok=True)
    settings.write_bytes(settings_backup)
after_exists, after_files, after_bytes = stats(pdata)
if before_exists and (not after_exists or before_files != after_files or before_bytes != after_bytes):
    raise SystemExit(PREFIX + ' PluginData changed before=' +
        str((before_files, before_bytes)) + ' after=' + str((after_files, after_bytes)))

installed_dll = installed / 'Plugins/AERISFlightControl.dll'
if sha(repo_dll) != sha(installed_dll):
    raise SystemExit(PREFIX + ' DLL SHA mismatch')
data = installed_dll.read_bytes()
for token, label in (
    (MARKER, 'Gilly calibration marker'),
    ('[R031][PTC_GILLY_ALGO] event=CALIBRATION_COMPLETE', 'Gilly calibration summary'),
    ('runtime_object_invocation_thread=MAIN_THREAD_ONLY', 'main-thread runtime invocation guard'),
    ('worker_invokes_runtime_object=false', 'worker runtime-object guard'),
    ('authority=PQS', 'PQS authority')):
    if not marker(data, token):
        raise SystemExit(PREFIX + ' installed DLL missing ' + label)

head = out(['git', 'rev-parse', 'HEAD'])
dll_sha = sha(installed_dll)
identity = (
    'aeris_candidate=' + MARKER + '\n'
    'branch=' + BRANCH + '\n'
    'source_head=' + head + '\n'
    'parent=' + PARENT + '\n'
    'runtime_change=PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW_ONLY\n'
    'runtime_object_invocation_thread=MAIN_THREAD_ONLY\n'
    'worker_invokes_runtime_object=NO\n'
    'terrain_db_write=NO\n'
    'producer_switch=NO\n'
    'terrain_authority=PQS\n'
    'gpu=NO\n'
    'certification=NO_SHADOW_ONLY\n'
    'install_mode=OVERLAY_PRESERVE_PLUGINDATA_AND_USER_SETTINGS\n'
    'plugin_data_before_files=' + str(before_files) + '\n'
    'plugin_data_before_bytes=' + str(before_bytes) + '\n'
    'plugin_data_after_files=' + str(after_files) + '\n'
    'plugin_data_after_bytes=' + str(after_bytes) + '\n'
    'shader_provenance=' + shader_provenance + '\n'
    'shader_file_count=' + str(len(shader_files)) + '\n'
    'dll_sha256=' + dll_sha + '\n')
(repo / 'AERISCandidateBuildIdentity.txt').write_text(identity)
(installed / 'AERISCandidateBuildIdentity.txt').write_text(identity)

print(PREFIX + ' PASS')
print('branch=' + branch)
print('head=' + head)
print('runtime_change=PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW_ONLY')
print('runtime_object_invocation_thread=MAIN_THREAD_ONLY')
print('worker_invokes_runtime_object=NO')
print('terrain_db_write=NO producer_switch=NO terrain_authority=PQS gpu=NO certification=NO')
print('plugin_data_files=' + str(after_files))
print('plugin_data_bytes=' + str(after_bytes))
print('shader_provenance=' + shader_provenance)
print('dll_sha256=' + dll_sha)
print('TEST: fully restart KSP to Main Menu and wait for [R031][PTC_GILLY_ALGO] event=CALIBRATION_COMPLETE')
