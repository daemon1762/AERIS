#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]

affine=ROOT/'Tools/verify_aeris23_witness_affine_source.py'
stagger=ROOT/'Tools/selftest_v01800_operation_health_staggered_exact_refresh.py'
for path in (affine,stagger):
    if not path.is_file():
        raise SystemExit('[AERIS23 STAGGER VERIFY] required verifier missing: '+str(path))
subprocess.run([sys.executable,str(affine)],cwd=str(ROOT),check=True)
subprocess.run([sys.executable,str(stagger)],cwd=str(ROOT),check=True)

r=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
b=(ROOT/'build_ubuntu.sh').read_text()
def require(ok,msg):
    if not ok:
        raise SystemExit('[AERIS23 STAGGER VERIFY] FAIL: '+msg)
    print('[PASS] '+msg)
def active_count(text,line):
    target=line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip()==target)

require('StaggeredExactRefreshSlotCount = 12' in r,'twelve-slot exact refresh distribution is present')
require('StaggeredExactRefreshMinimumSeconds = 2.80f' in r and
        'StaggeredExactRefreshSlotSeconds = 0.10f' in r,'2.80-3.90 second stagger window is present')
require('AffineWitnessMaximumAgeSeconds = 4.00f' in r,'4.00 second hard freshness authority is retained')
require('oh_stagger_due=' in r and 'oh_stagger_defer=' in r,'stagger runtime telemetry is published')
require('oh_stagger_back_peak=' in r and 'oh_stagger_back_samples=' in r and
        'oh_stagger_back_gt8=' in r,'READY steady-state exact burst telemetry is published')
require('staggerBurstTelemetryEligible = frontBufferValid && requestedViewReady' in r,
        'bootstrap/loading exact work is excluded from steady burst telemetry')
require('AffineWitnessAcceptancePixels = 0.08f' in r,'0.08 px affine witness gate is unchanged')
require('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in r,'fixed 10 Hz authority remains intact')
require('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,'visual RenderTexture authority remains intact')
# Historical comments may retain earlier candidate names. Executable lines alone are authority.
new='CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
old='CANDIDATE_NAME="AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION"'
new_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
old_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_witness_affine_source.py"'
require(active_count(b,new)==1,'build candidate identity is exactly STAGGERED_EXACT_REFRESH')
require(active_count(b,old)==0,'no executable stale Affine candidate identity remains')
require(active_count(b,new_verify)==1,'build preflight requires the stagger verifier itself')
require(active_count(b,old_verify)==0,'no executable stale Affine verifier remains')
print('[AERIS23 CANDIDATE VERIFY] AFFINE_STAGGERED_EXACT_REFRESH SOURCE+IDENTITY PASS')
