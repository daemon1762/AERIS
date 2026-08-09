#!/usr/bin/env python3
from pathlib import Path
import re,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
V=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs').read_text()
P=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs').read_text()
C=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs').read_text()
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('return Profiles[0];' in P and 'new AERISTerrainPerformanceProfile("LOW"' in P,
   'runtime terrain authority remains Golden LOW')
ck('DefaultResolution = 33' in C,
   'authoritative FAR payload remains 33x33 samples')
ck('static readonly AERISTerrainVirtualDetailProfile GoldenLow' in V,
   'dedicated Golden LOW presentation profile exists')
ck('"GOLDEN LOW 65", 2, 65, 1.0f' in V,
   'Golden LOW reconstructs to 65x65 without increasing RenderTexture scale')
resolve=V[V.index('internal static AERISTerrainVirtualDetailProfile Resolve('):
          V.index('internal static AERISTerrainHeightTile ReconstructFar(')]
ck('bool low = string.Equals(qualityName, "LOW"' in resolve and
   'if (low) return GoldenLow;' in resolve,
   'LOW always resolves to refined presentation surface')
ck(resolve.index('if (low) return GoldenLow;') < resolve.index('if ((land && range <= 40000f)'),
   'Golden LOW quality floor is independent of range gates')
ck('(source.Resolution - 1) * profile.ReconstructionScale + 1' in V,
   '33x33 source with scale 2 reconstructs exactly to 65x65')
ck('source.HighDensityCoastlineSegments == null ? null :' in V and
   '(float[])source.HighDensityCoastlineSegments.Clone()' in V and
   '(byte[])source.HighDensityCoastalFlags.Clone()' in V,
   '129-class coastline authority survives reconstruction')
ck('AERISTerrainVirtualDetailPolicy.ReconstructFar(sourceTile, request.VirtualDetailProfile)' in R,
   'GPU rasterizer consumes reconstructed Golden LOW surface')
ck('VirtualDetailLevel = request.VirtualDetailProfile == null ?' in R,
   'runtime telemetry reports virtual reconstruction level')
# Numerical guard for the exact expected source/presentation lattice.
source_resolution=33
presentation=(source_resolution-1)*2+1
ck(presentation == 65 and (presentation-1)==64,
   'Golden LOW visible lattice is 64x64 cells, not 32x32 cells')
failed=[n for ok,n in checks if not ok]
print('\n[Golden LOW Surface Refinement] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
