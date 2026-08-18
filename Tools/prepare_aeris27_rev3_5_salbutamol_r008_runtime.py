#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT=Path(__file__).resolve().parents[1]
R=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
Z=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
T=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX='[AERIS27 REV3.5 SALBUTAMOL SULFATE R008 RUNTIME]'
R001='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
R005='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'
R006='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
HF2='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
HF3='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
R007='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'
R008='AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
TTY=sys.stdout.isatty(); GREEN='\033[1;32m' if TTY else ''; RED='\033[1;31m' if TTY else ''; CYAN='\033[1;36m' if TTY else ''; YELLOW='\033[1;33m' if TTY else ''; MAGENTA='\033[1;35m' if TTY else ''; RESET='\033[0m' if TTY else ''


def info(msg): print(CYAN+PREFIX+RESET+' '+msg)
def run(args):
    args=[str(x) for x in args]; info('$ '+' '.join(args)); subprocess.run(args,cwd=str(ROOT),check=True)
def marker_present(path,marker):
    try: return marker in path.read_text()
    except OSError: return False
def marker_in_bytes(data,text): return text.encode('utf-8') in data or text.encode('utf-16le') in data
def sha256(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''): h.update(block)
    return h.hexdigest()

def reconstruct_r005():
    scripts=(
        'apply_aeris25_gpu_dynamic_terrain_colour_ready.py','apply_aeris25_chunk_cull_guard_hotfix.py',
        'apply_aeris25_temporal_foundation_overscan_hotfix.py','apply_aeris25_foundation_cull_bypass_hotfix.py',
        'apply_aeris25_renderable_entry_gate_hotfix.py','apply_aeris25_gpu_vertex_reject_diagnostics_hotfix.py',
        'apply_aeris25_snapshot_mesh_lifetime_guard_hotfix.py','apply_aeris25_content_generation_burst_governor_hotfix.py',
        'apply_aeris25_persistent_presentation_batching.py','apply_aeris25_main_thread_commit_governor.py',
        'apply_aeris25_staged_main_thread_commit_hotfix.py','fix_aeris25_phase6_002_inherited_selftests.py',
        'apply_aeris25_authoritative_publication_lifetime_hotfix.py','fix_aeris25_phase6_002_inherited_selftests.py',
        'fix_aeris25_phase6_003_inherited_selftests.py','apply_aeris26_rev003_observer.py',
        'apply_aeris27_rev3_5_salbutamol_r001_compile_hotfix1.py','apply_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py',
        'apply_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py','apply_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py',
        'apply_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py')
    for s in scripts: run([sys.executable,ROOT/'Tools'/s])

parser=argparse.ArgumentParser(description='Prepare/install AERIS27 REV3.5 R008 Current Foundation Upstream Priority. R008 establishes the exact current request set before worker admission, cancels obsolete terrain raster work/results, and schedules current FAR before other LODs. Shared scheduler workers/fairness, commit budget, 10Hz, exact range and Golden quality remain unchanged.')
parser.add_argument('ksp_path'); args=parser.parse_args(); ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir(): raise SystemExit(RED+PREFIX+' KSP path not found: '+str(ksp)+RESET)

if not marker_present(R,R005): reconstruct_r005()
for marker,script,path in (
    (R006,'apply_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',R),
    (HF1,'apply_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py',R),
    (HF2,'apply_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py',R),
    (HF3,'apply_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py',T),
    (HF4,'apply_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py',R),
    (R007,'apply_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',R),
    (R008,'apply_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py',R)):
    if not marker_present(path,marker): run([sys.executable,ROOT/'Tools'/script])
    else: info('existing '+marker+' generated tree detected')

verifiers=(
    'verify_aeris25_authoritative_publication_lifetime_hotfix.py','verify_aeris27_rev3_5_salbutamol_resumable_prepare.py',
    'verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py','verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py',
    'verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py','verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py',
    'verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py','verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py',
    'verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py','verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py',
    'verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py','verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',
    'verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py')
for v in verifiers: run([sys.executable,ROOT/'Tools'/v])
run(['git','diff','--check'])
for generated in (ROOT/'Source/AERISFlightControl/bin',ROOT/'Source/AERISFlightControl/obj'):
    if generated.exists(): info('removing stale build directory: '+str(generated)); shutil.rmtree(generated)
run(['bash',ROOT/'build_ubuntu.sh',ksp])

source_dll=ROOT/'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'; installed_root=ksp/'GameData/AERISFlightControl'; installed_dll=installed_root/'Plugins/AERISFlightControl.dll'; identity=installed_root/'AERISCandidateBuildIdentity.txt'
for p in (source_dll,installed_dll,identity):
    if not p.is_file(): raise SystemExit(RED+PREFIX+' installed artifact missing: '+str(p)+RESET)
identity_text=identity.read_text(errors='replace'); dll=installed_dll.read_bytes(); git_head=subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()
ids=((R001,'rev3_5_variant'),(R002,'rev3_5_r002_variant'),(R003,'rev3_5_r003_variant'),(R004,'rev3_5_r004_variant'),(R005,'rev3_5_r005_variant'),(R006,'rev3_5_r006_variant'),(HF1,'rev3_5_r006_hotfix1'),(HF2,'rev3_5_r006_hotfix2'),(HF3,'rev3_5_r006_hotfix3'),(HF4,'rev3_5_r006_hotfix4'),(R007,'rev3_5_r007_variant'),(R008,'rev3_5_r008_variant'))
checks=[(sha256(source_dll)==sha256(installed_dll),'built/installed DLL SHA')]
for marker,key in ids: checks.append(((key+'='+marker) in identity_text,key+' identity'))
checks.extend((
    (('git='+git_head) in identity_text,'identity git HEAD'),
    (marker_in_bytes(dll,R008),'DLL embeds R008 marker'),
    (marker_in_bytes(dll,'oh_rev35_r008_reconcile=') and marker_in_bytes(dll,'oh_rev35_r008_scheduler_cancel=') and marker_in_bytes(dll,'oh_rev35_r008_raster_completed_drop='),'DLL embeds R008 upstream telemetry'),
    (marker_in_bytes(dll,R007) and marker_in_bytes(dll,'oh_rev35_r007_chain='),'DLL retains R007'),
    (marker_in_bytes(dll,HF4) and marker_in_bytes(dll,'oh_rev35_r006_hf4_colour_pool_hit='),'DLL retains HF4'),
    (marker_in_bytes(dll,HF3) and marker_in_bytes(dll,'hf3_preserve='),'DLL retains HF3'),
    (not marker_in_bytes(dll,'WaitManagedPreparation') and not marker_in_bytes(dll,'ResidentPreparedPresentation') and not marker_in_bytes(dll,'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'),'DLL excludes rejected mechanisms')))
failed=[]
for ok,label in checks:
    print((GREEN if ok else RED)+('[PASS] ' if ok else '[FAIL] ')+label+RESET)
    if not ok: failed.append(label)
if failed: raise SystemExit(RED+PREFIX+' INSTALL IDENTITY FAIL: '+', '.join(failed)+RESET)
print(GREEN+PREFIX+' INSTALL IDENTITY MATCH=YES'+RESET)
print('r008='+R008); print('git='+git_head); print('dll_sha256='+sha256(installed_dll))
print(CYAN+'R008 ACTIVE:'+RESET+' complete current request prepass -> stale raster reconcile/cancel -> FAR-first worker admission.')
print(MAGENTA+'R008 OBSERVERS:'+RESET+' pump_suppress/pending_cancel/far_schedule/reconcile/raster_pending_cancel/raster_completed_drop/scheduler_cancel.')
print(YELLOW+'R008 FROZEN:'+RESET+' shared scheduler worker count/fairness unchanged; R007/HF4/HF3/R003, R004 2ms, R005 source64, 10Hz, exact range and Golden quality retained.')
