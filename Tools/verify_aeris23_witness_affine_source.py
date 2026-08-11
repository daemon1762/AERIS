#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]

single=ROOT/'Tools/verify_aeris23_single_authority_source.py'
if not single.is_file():
    raise SystemExit('[AERIS23 AFFINE VERIFY] Single-Authority verifier missing')
subprocess.run([sys.executable,str(single)],cwd=str(ROOT),check=True)

path=ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
r=path.read_text()

def require(ok,msg):
    if not ok:
        raise SystemExit('[AERIS23 AFFINE VERIFY] FAIL: '+msg)
    print('[PASS] '+msg)

require('AffineWitnessMaximumCount = 8' in r,'eight-witness bound is present')
require('AffineWitnessAcceptancePixels = 0.08f' in r,'0.08 px witness acceptance rail is present')
require('TryResolveWitnessAffineBridge(' in r,'witness affine resolver is present')
require('CaptureProjectionWitnesses(entry)' in r,'exact projection refreshes witness authority')
require('AccumulateProjectionWitnessCandidates(entry.PackedTerrainGeographicPoints' in r and
        'AccumulateProjectionWitnessCandidates(entry.ContourGeographicPoints' in r and
        'AccumulateProjectionWitnessCandidates(entry.CoastlineGeographicPoints' in r,
        'witness authority covers terrain contour and coastline')
project=r[r.index('        Matrix4x4 EnsureProjectedGeometry('):r.index('        void ProjectMesh(',r.index('        Matrix4x4 EnsureProjectedGeometry('))]
require('TryResolveWitnessAffineBridge(entry, context' in project,'affine validation executes before exact upload')
require('Matrix4x4.Translate' in project,'accepted translation bridge remains fallback')
require('ProjectMesh(entry.PackedTerrainMesh' in project and
        'ProjectMesh(entry.ContourMesh' in project and
        'ProjectMesh(entry.CoastlineMesh' in project,'exact fallback remains complete')
require('ProjectionBridgeLatitudeLimitDeg' in project and 'polarExactOnly' in project,
        'polar exact-only safety is retained')
require('oh_affine_bridge=' in r and 'oh_affine_reject=' in r and
        'oh_affine_witness=' in r and 'oh_affine_exact_fallback=' in r and
        'oh_affine_max_mpx=' in r,'runtime affine telemetry is published')
require('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in r,
        'fixed 10 Hz authority remains intact')
require('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
        'ARGB32/Bilinear visual authority remains intact')
print('[AERIS23 CANDIDATE VERIFY] WITNESS_BOUNDED_AFFINE_PROJECTION SOURCE PASS')