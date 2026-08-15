#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = "AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR"
CODENAME = "ATRO" + "PINE"
ACCEPTED_REVISIONS = ("OH_PHASE4_001", "OH_PHASE4_002", "OH_PHASE4_003", "OH_PHASE4_004")
BUNDLE_WINDOWS = "aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
BUNDLE_LINUX = "aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
B = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
S = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
E = (ROOT / "GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs").read_text()
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
C = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()
U = (ROOT / "build_ubuntu.sh").read_text()
checks=[]
def ck(v,n):
    ok=bool(v); checks.append((ok,n)); print(("[PASS] " if ok else "[FAIL] ")+n)
ck('internal const string Codename = "'+CODENAME+'";' in M and any(('internal const string Revision = "'+revision+'";') in M for revision in ACCEPTED_REVISIONS) and 'internal const string Candidate = "'+CANDIDATE+'";' in M and ('codename = '+CODENAME) in C,'AERIS25 Operation Health ATROPINE identity is authoritative')
ck(('CANDIDATE_NAME="'+CANDIDATE+'"') in U and 'AERIS25 OPERATION HEALTH PHASE 4 '+CODENAME+' GPU DYNAMIC TERRAIN COLOUR' in U and 'AERIS25 — OPERATION HEALTH PHASE 4 '+CODENAME+' — GPU DYNAMIC TERRAIN COLOUR' in U,'build and in-game identity are AERIS25-specific')
ck(BUNDLE_WINDOWS in B and BUNDLE_LINUX in B and BUNDLE_WINDOWS in E and BUNDLE_LINUX in E and BUNDLE_WINDOWS in U and BUNDLE_LINUX in U,'AERIS25 shader bundles are separate from preserved AERIS24 bundles')
ck('float3 terrainSemantic : TEXCOORD2;' in S and '_AerisTerrainSemanticMode' in S and '_AerisTerrainDisplayMode' in S and '_AerisTerrainPreset' in S and '_AerisAircraftAltitudeMeters' in S,'shader consumes persistent terrain semantics plus dynamic uniforms')
ck('AerisRelativeColour' in S and 'clearance <= 30.0' in S and 'clearance <= 300.0' in S and 'clearance <= 600.0' in S,'shader preserves frozen REL thresholds')
ck('AerisTopographicColour' in S and '(elevation + 500.0) / 12500.0' in S and 'AerisGradient' in S,'shader preserves frozen TOPO mapping')
ck('shadeByte / 227.0' in S and '0.30' in S and '0.55' in S and '0.94' in S and '1.035' in S,'shader preserves frozen shade law')
ck('AerisByteColour(0, 20, 70)' in S and 'AerisByteColour(8, 52, 118)' in S,'shader preserves frozen water colours')
ck('_AerisTerrainSemanticMode > 0.5' in S and 'input.color * _Color' in S,'contour/coastline retain legacy vertex-colour path')
ck('GpuDynamicColourAttributesReady' in R and 'GpuDynamicColourRejected' in R and 'gpuDynamicTerrainSemanticScratch' in R,'Entry tracks one-time GPU semantic residency')
ck('SetUVs(2, gpuDynamicTerrainSemanticScratch)' in R and 'new Vector3(0f, 255f, 0f)' in R and 'entry.LandElevationMeters[i], entry.LandShade[i], 1f' in R,'packed Entry uploads elevation/shade/land-water semantics once')
ck('if (!EnsureGpuDynamicTerrainColourAttributes(entry))' in R and 'entry.GpuVertexProjectionRejected = true;' in R,'semantic failure falls through before GPU exact authority')
draw_start=R.index('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
draw_end=R.index('        static void EnsurePackedTerrainColours(Entry entry,',draw_start)
draw=R[draw_start:draw_end]
ck('entry.GpuDynamicColourAttributesReady' in draw and 'if (!gpuEntry)\n                EnsurePackedTerrainColours' in draw and 'operationHealthGpuDynamicCpuColourBypasses++' in draw,'GPU draw bypasses CPU colour rewrite; fallback keeps it')
ck(draw.index('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') < draw.index('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)') < draw.index('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)'),'per-Entry terrain-contour-coast order unchanged')
ck('effectiveMode, gpuPreset' in R and '(float)vessel.altitude' in R,'BACK sends dynamic colour uniforms')
ck('Mathf.RoundToInt(aircraftAltitudeAslMeters / 5f) * 5f' in B,'REL altitude keeps 5 m quantization')
ck('oh_gpu_dynamic_colour=' in R and 'oh_gpu_dynamic_semantic_upload=' in R and 'oh_gpu_dynamic_semantic_fail=' in R and 'oh_gpu_dynamic_cpu_colour_bypass=' in R,'dynamic-colour runtime telemetry is present')
ck('GPU_DYNAMIC_SEMANTIC' in R,'alignment telemetry identifies GPU dynamic colour source')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'fixed 10 Hz authority unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'Golden render target unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,'Runway Map Lock and coverage telemetry remain')
ck('CPU_EXACT_REQUESTED' in B and 'DisableAndFallback' in B,'CPU exact fail-closed fallback remains')
frozen=['Source/AERISFlightControl/AA','Source/AERISFlightControl/Autopilot','Source/AERISFlightControl/Protect','Source/AERISFlightControl/Landing']
try:
    changed=subprocess.check_output(['git','-C',str(ROOT),'diff','--name-only','HEAD','--']+frozen,text=True).strip().splitlines()
except Exception:
    changed=['GIT_DIFF_UNAVAILABLE']
ck(changed==[],'AA/AP/PROTECT/LAND working-tree edits remain NONE')
failed=[n for ok,n in checks if not ok]
print("\n[AERIS25 GPU DYNAMIC TERRAIN COLOUR] %d/%d PASS" % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+'; '.join(failed)); raise SystemExit(1)
print('[AERIS25 GPU DYNAMIC TERRAIN COLOUR] STATIC PASS')
