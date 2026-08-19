#!/usr/bin/env python3
from pathlib import Path
import argparse, hashlib, shutil, subprocess, sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX = '[AERIS28 REV3.5 SALBUTAMOL SULFATE R011 RUNTIME]'
R005='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'
R006='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
HF2='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
HF3='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
R007='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'
R008='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
R009='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE'
R010='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011='AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'

TTY=sys.stdout.isatty(); GREEN='\033[1;32m' if TTY else ''; RED='\033[1;31m' if TTY else ''; CYAN='\033[1;36m' if TTY else ''; YELLOW='\033[1;33m' if TTY else ''; RESET='\033[0m' if TTY else ''
def info(m): print(CYAN+PREFIX+RESET+' '+m)
def run(args):
    args=[str(x) for x in args]; info('$ '+' '.join(args));
    subprocess.run(args,cwd=str(ROOT),check=True)
def has(path,marker):
    try: return marker in path.read_text()
    except OSError: return False
def marker_in_bytes(data,text):
    return text.encode() in data or text.encode('utf-16le') in data
def sha256(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''): h.update(block)
    return h.hexdigest()

def reconstruct_r005():
    scripts=(
      'apply_aeris25_gpu_dynamic_terrain_colour_ready.py',
      'apply_aeris25_chunk_cull_guard_hotfix.py',
      'apply_aeris25_temporal_foundation_overscan_hotfix.py',
      'apply_aeris25_foundation_cull_bypass_hotfix.py',
      'apply_aeris25_renderable_entry_gate_hotfix.py',
      'apply_aeris25_gpu_vertex_reject_diagnostics_hotfix.py',
      'apply_aeris25_snapshot_mesh_lifetime_guard_hotfix.py',
      'apply_aeris25_content_generation_burst_governor_hotfix.py',
      'apply_aeris25_persistent_presentation_batching.py',
      'apply_aeris25_main_thread_commit_governor.py',
      'apply_aeris25_staged_main_thread_commit_hotfix.py',
      'fix_aeris25_phase6_002_inherited_selftests.py',
      'apply_aeris25_authoritative_publication_lifetime_hotfix.py',
      'fix_aeris25_phase6_002_inherited_selftests.py',
      'fix_aeris25_phase6_003_inherited_selftests.py',
      'apply_aeris26_rev003_observer.py',
      'apply_aeris27_rev3_5_salbutamol_r001_compile_hotfix1.py',
      'apply_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py',
      'apply_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py',
      'apply_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py',
      'apply_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py')
    for script in scripts: run([sys.executable,ROOT/'Tools'/script])

p=argparse.ArgumentParser(description='Reconstruct R010 formal runtime, overlay read-only R011 turning-view churn observer, verify, build and install.')
p.add_argument('ksp_path'); a=p.parse_args(); ksp=Path(a.ksp_path).expanduser().resolve()
if not ksp.is_dir(): raise SystemExit(RED+PREFIX+' KSP path not found: '+str(ksp)+RESET)

if not has(R,R005): reconstruct_r005()
lineage=(
 (R006,'apply_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',R),
 (HF1,'apply_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py',R),
 (HF2,'apply_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py',R),
 (HF3,'apply_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py',T),
 (HF4,'apply_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py',R),
 (R007,'apply_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',R),
 (R008,'apply_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py',R),
 (R009,'apply_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py',R),
 (R010,'apply_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py',R))
for marker,script,path in lineage:
    if not has(path,marker): run([sys.executable,ROOT/'Tools'/script])
    else: info('existing '+marker+' generated tree detected')

verifiers=(
 'verify_aeris25_authoritative_publication_lifetime_hotfix.py',
 'verify_aeris27_rev3_5_salbutamol_resumable_prepare.py',
 'verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py',
 'verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py',
 'verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py',
 'verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py',
 'verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',
 'verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py',
 'verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py',
 'verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py',
 'verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py',
 'verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',
 'verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py',
 'verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py',
 'verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py')
for verifier in verifiers: run([sys.executable,ROOT/'Tools'/verifier])

run([sys.executable,ROOT/'Tools/apply_aeris28_rev3_5_salbutamol_r011_turning_view_churn_observer.py'])
run([sys.executable,ROOT/'Tools/verify_aeris28_rev3_5_salbutamol_r011_turning_view_churn_observer.py'])
run(['git','diff','--check'])
for d in (ROOT/'Source/AERISFlightControl/bin',ROOT/'Source/AERISFlightControl/obj'):
    if d.exists(): info('removing stale build directory: '+str(d)); shutil.rmtree(d)
run(['bash',ROOT/'build_ubuntu.sh',ksp])

source=ROOT/'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed=ksp/'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
identity=ksp/'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
for path in (source,installed,identity):
    if not path.is_file(): raise SystemExit(RED+PREFIX+' installed artifact missing: '+str(path)+RESET)
ident=identity.read_text(errors='replace'); dll=installed.read_bytes()
checks=[
 (sha256(source)==sha256(installed),'built/installed DLL SHA'),
 (('rev3_5_r010_variant='+R010) in ident,'R010 identity retained'),
 (('rev3_5_r011_variant='+R011) in ident,'R011 identity installed'),
 (marker_in_bytes(dll,R010),'DLL embeds R010 marker'),
 (marker_in_bytes(dll,'OH_REV3_5_R011_TURN_CHURN'),'DLL embeds R011 observer marker'),
 (not marker_in_bytes(dll,'WaitManagedPreparation') and
  not marker_in_bytes(dll,'ResidentPreparedPresentation') and
  not marker_in_bytes(dll,'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'),
  'DLL excludes rejected mechanisms')]
failed=[]
for ok,label in checks:
    print((GREEN if ok else RED)+('[PASS] ' if ok else '[FAIL] ')+label+RESET)
    if not ok: failed.append(label)
if failed: raise SystemExit(RED+PREFIX+' INSTALL IDENTITY FAIL: '+', '.join(failed)+RESET)
print(GREEN+PREFIX+' INSTALL IDENTITY MATCH=YES'+RESET)
print('r010='+R010); print('r011='+R011); print('dll_sha256='+sha256(installed))
print(YELLOW+'R011 CONTRACT:'+RESET+' measurement-only observer; R010 rendering/control/publication behavior unchanged.')
