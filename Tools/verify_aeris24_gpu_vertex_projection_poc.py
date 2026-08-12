#!/usr/bin/env python3
from pathlib import Path
import math
import struct
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
B = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
P = (ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj").read_text()
S = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
E = (ROOT / "GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs").read_text()
BUILD = (ROOT / "build_ubuntu.sh").read_text()
PV = (ROOT / "GpuAssets/ProjectSettings/ProjectVersion.txt").read_text()

checks = []
def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)


def active_count(text, line):
    target = line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip() == target)

ck(active_count(BUILD, 'CANDIDATE_NAME="AERIS24_GPU_VERTEX_PROJECTION_POC"') == 1,
   "singular active AERIS24 GPU Vertex candidate identity")
ck(active_count(BUILD, 'CANDIDATE_NAME="AERIS23_OH_PENICILLIN"') == 0,
   "PENICILLIN executable identity promoted rather than duplicated")
ck('verify_aeris24_gpu_vertex_projection_poc.py' in BUILD,
   "normal build invokes GPU Vertex candidate verifier")
ck('gpu_shader_bundle_sha256=' in BUILD and 'GPU_SHADER_BUNDLE_SHA256=' in BUILD,
   "build identity includes platform shader-bundle SHA")
ck('Terrain\\AERISNdGpuVertexProjectionBackend.cs' in P,
   "GPU vertex runtime backend is compiled")
ck('2019.4.18f1' in PV,
   "shader AssetBundle project pinned to KSP Unity 2019.4.18f1")
ck('BuildTarget.StandaloneWindows64' in E and 'BuildTarget.StandaloneLinux64' in E,
   "shader builder supports current Proton/WindowsPlayer and native Linux KSP")

# Runtime/backend fail-closed contract.
ck('AssetBundle.LoadFromFile' in B and 'LoadAllAssets<Shader>' in B,
   "runtime loads precompiled shader AssetBundle without runtime shader compilation")
ck('CPU EXACT FALLBACK' in B and 'DisableAndFallback' in B and
   'ValidatePassesOrFallback' in B,
   "missing/unsupported/rejected shader fails closed to CPU exact")
ck('ReleaseForSuspension' in B,
   "Terrain OFF/suspension releases custom GPU presentation resources")
ck('new Thread' not in B and 'Task.Run' not in B and 'ThreadPool' not in B,
   "GPU backend creates no asynchronous runtime or worker authority")

# Exact GPU-source contract.
ck('float3 geographicUnit : TEXCOORD1' in S and
   'mesh.SetUVs(1, gpuVertexGeographicScratch);' in R,
   "immutable geographic XYZ is carried by static TEXCOORD1")
for token in ('1.0 / 6.0', '3.0 / 40.0', '5.0 / 112.0',
              '35.0 / 1152.0', '63.0 / 2816.0',
              'atan2(radial, centerDot) / radial'):
    ck(token in S, "shader exact spherical formula: " + token)
ck('_AerisCenter' in S and '_AerisEast' in S and '_AerisNorth' in S and
   '_AerisRadiusMeters' in S and '_AerisHorizontalMeters' in S and
   '_AerisVerticalMeters' in S and '_AerisAnchorRenderV' in S,
   "projection center/basis/range/anchor are uniform-only runtime inputs")
ck('UnityObjectToClipPos(float4(u, renderV, 0.0, 1.0))' in S,
   "vertex shader emits exact N-UP ND coordinates into existing map matrix")
ck('Frame Generation' not in R and '60 Hz' not in S and '120 Hz' not in S,
   "GPU vertex path does not introduce frame-generation authority")

# Generated renderer must bypass full CPU projection only when the GPU path is active.
project_start = R.index('Matrix4x4 EnsureProjectedGeometry(')
project_end = R.index('void ProjectMesh(', project_start)
project = R[project_start:project_end]
ck(project.index('gpuVertexProjection.Active') < project.index('bool structuralProjectionChange'),
   "GPU exact bypass occurs before CPU affine/stagger/exact work")
ck('ProjectMesh(entry.PackedTerrainMesh' in project and
   'ProjectMesh(entry.ContourMesh' in project and
   'ProjectMesh(entry.CoastlineMesh' in project,
   "complete CPU exact fallback remains intact")
ck('TryResolveWitnessAffineBridge(entry, context' in project and
   'ResolveStaggeredExactRefreshDeadlineSeconds(entry)' in project,
   "Witness Affine and Stagger remain intact for CPU fallback")
ck('operationHealthGpuVertexExactBypasses++' in project,
   "GPU exact bypass is directly observable")
ck('oh_gpu_vertex_projection=' in R and 'oh_gpu_vertex_attr_upload=' in R and
   'oh_gpu_vertex_exact_bypass=' in R and 'oh_gpu_vertex_back_frames=' in R,
   "runtime exports GPU vertex activation/upload/bypass telemetry")

# Golden authority and painter-order invariants.
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   "authoritative ND presentation remains fixed at 10 Hz")
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   "ARGB32/Bilinear presentation authority unchanged")
draw_start = R.index('bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
draw_end = R.index('static void EnsurePackedTerrainColours', draw_start)
draw = R[draw_start:draw_end]
terrain_pos = draw.index('Graphics.DrawMeshNow(entry.PackedTerrainMesh')
contour_pos = draw.index('Graphics.DrawMeshNow(entry.ContourMesh')
coast_pos = draw.index('Graphics.DrawMeshNow(entry.CoastlineMesh')
ck(terrain_pos < contour_pos < coast_pos,
   "Golden Entry painter order remains terrain -> contour -> coastline")
ck('gpuVertexProjectionBackFailure' in R and
   'return rendered && !gpuVertexProjectionBackFailure;' in R,
   "runtime shader failure cannot promote a partial BACK to FRONT")
ck('runwayMapLockErrorPx=' in R and 'lastRunwayMapLockErrorPixels > 1.0f' in R,
   "Runway Map Lock rejection remains unchanged")
ck('visualCoverage=' in R,
   "visualCoverage Golden diagnostic remains present")

# The new candidate must not touch flight-control authority source.
frozen = [
    'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect',
    'Source/AERISFlightControl/Landing',
]
try:
    changed = subprocess.check_output(
        ['git', '-C', str(ROOT), 'diff', '--name-only', 'HEAD', '--'] + frozen,
        text=True).strip().splitlines()
except Exception:
    changed = ['GIT_DIFF_UNAVAILABLE']
ck(changed == [], "AA/AP/PROTECT/LAND working-tree edits remain NONE")

# Offline numerical parity: emulate shader float inputs/intermediates against the existing
# double CPU projection over representative Kerbin centers, bearings, and all ND ranges.
def f32(x):
    return struct.unpack('f', struct.pack('f', float(x)))[0]

def basis(lat, lon):
    lat = math.radians(lat); lon = math.radians(lon)
    c = math.cos(lat)
    center = (c * math.cos(lon), c * math.sin(lon), math.sin(lat))
    east = (-math.sin(lon), math.cos(lon), 0.0)
    north = (-math.sin(lat) * math.cos(lon),
             -math.sin(lat) * math.sin(lon), math.cos(lat))
    return center, east, north

def destination(center, east, north, angle, bearing):
    # Tangent direction; rotate the center unit vector by angular distance on the sphere.
    t = tuple(east[i] * math.sin(bearing) + north[i] * math.cos(bearing)
              for i in range(3))
    return tuple(center[i] * math.cos(angle) + t[i] * math.sin(angle)
                 for i in range(3))

def cpu_project(p, center, east, north, radius, h, v):
    eu = sum(p[i] * east[i] for i in range(3))
    nu = sum(p[i] * north[i] for i in range(3))
    r2 = max(0.0, eu * eu + nu * nu)
    if r2 <= 0.18:
        factor = 1.0 + r2 * (1.0/6.0 + r2 * (3.0/40.0 + r2 *
            (5.0/112.0 + r2 * (35.0/1152.0 + r2 * 63.0/2816.0))))
    else:
        radial = math.sqrt(r2)
        dot = sum(p[i] * center[i] for i in range(3))
        factor = 1.0 if radial <= 1e-12 else math.atan2(radial, dot) / radial
    return 0.5 + eu * radius * factor / h, eu, nu, factor

def gpu_project_f32(p, center, east, north, radius, h, v):
    p = tuple(f32(x) for x in p)
    center = tuple(f32(x) for x in center)
    east = tuple(f32(x) for x in east)
    north = tuple(f32(x) for x in north)
    eu = f32(f32(f32(p[0]*east[0]) + f32(p[1]*east[1])) + f32(p[2]*east[2]))
    nu = f32(f32(f32(p[0]*north[0]) + f32(p[1]*north[1])) + f32(p[2]*north[2]))
    r2 = f32(max(0.0, f32(eu*eu) + f32(nu*nu)))
    if r2 <= f32(0.18):
        factor = f32(1.0 + r2 * f32(1.0/6.0 + r2 * f32(3.0/40.0 +
            r2 * f32(5.0/112.0 + r2 * f32(35.0/1152.0 +
            r2 * f32(63.0/2816.0))))))
    else:
        radial = f32(math.sqrt(r2))
        dot = f32(f32(f32(p[0]*center[0]) + f32(p[1]*center[1])) +
                  f32(p[2]*center[2]))
        factor = f32(1.0 if radial <= 1e-12 else math.atan2(radial, dot) / radial)
    east_m = f32(f32(eu * f32(radius)) * factor)
    north_m = f32(f32(nu * f32(radius)) * factor)
    u = f32(0.5 + f32(east_m / f32(h)))
    vv = f32(0.5 + f32(north_m / f32(v)))
    return u, vv

max_px = 0.0
radius = 600000.0
for lat in (-60.0, -30.0, 0.0, 30.0, 60.0):
    for lon in (0.0, 90.0, 179.0):
        center, east, north = basis(lat, lon)
        for nd_range in (10000.0, 20000.0, 40000.0, 80000.0, 160000.0):
            horizontal = nd_range * 1.30
            vertical = nd_range
            for frac in (0.0, 0.25, 0.50, 0.85, 1.0):
                angle = (nd_range * frac) / radius
                for deg in range(0, 360, 45):
                    p = destination(center, east, north, angle, math.radians(deg))
                    cu, _, cnu, cf = cpu_project(p, center, east, north,
                                                 radius, horizontal, vertical)
                    cv = 0.5 + cnu * radius * cf / vertical
                    gu, gv = gpu_project_f32(p, center, east, north,
                                             radius, horizontal, vertical)
                    dx = (gu - cu) * 366.0
                    dy = (gv - cv) * 188.0
                    max_px = max(max_px, math.sqrt(dx*dx + dy*dy))
ck(max_px <= 0.04,
   "float shader reference stays within 0.04 px of CPU double exact (max %.6f px)" % max_px)

failed = [name for ok, name in checks if not ok]
print("\n[AERIS24 GPU VERTEX PROJECTION] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print("FAILED: " + "; ".join(failed))
    raise SystemExit(1)
print("[AERIS24 GPU VERTEX PROJECTION] SOURCE+SAFETY+MATH PASS")
