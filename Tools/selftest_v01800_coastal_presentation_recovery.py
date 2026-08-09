#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
V=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs').read_text()
C=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs').read_text()
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs').read_text()
P=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('return Profiles[0];' in P,'Golden LOW runtime authority remains fixed')
ck('GOLDEN LOW 65' not in V and 'if (low) return GoldenLow;' not in V,
   'failed all-tile 65x65 reconstruction is removed')
ck('BuildTopologyPreservingCoastalPresentationMask' in C,
   'coastal-only presentation refinement exists')
ck('if (fa == fb && fa == fc && fa == fd)' in C,
   'uniform parent cells cannot invent coastline')
ck('fx + fy <= 1f' in C and 'sd * (fx + fy - 1f)' in C,
   'subdivision follows existing FAR triangle topology')
ck('row % factor == 0 && column % factor == 0' in C,
   'original source-grid classes are copied exactly')
ck('bool highDensityBoundary' in R and
   'presentationCoastalFlags = highDensityBoundary ?' in R and
   'tile.HighDensityCoastalFlags : null' in R,
   'persisted Candidate11 HD mask remains first authority')
ck('!highDensityBoundary && tile.Key.Lod == AERISTerrainTileLod.Far' in R,
   'synthetic refinement is FAR coastal fallback only')
ck('coastalPresentationTile = tile.CloneImmutable()' in R,
   'synthetic presentation never mutates source tile authority')
ck('BuildSparseCoastalCorrections' in R and 'presentationCoastalFlags' in R,
   'coastline and sparse fill share selected presentation authority')
ck('BuildFromClassMask' in R and 'presentationCoastalResolution' in R,
   'coastline line consumes selected presentation authority')
ck('CoastlineResolution = highDensityBoundary ?' in R,
   'synthetic fallback is not reported as persisted HD authority')
failed=[n for ok,n in checks if not ok]
print('\n[Coastal Presentation Recovery] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
 print('FAILED: '+', '.join(failed)); raise SystemExit(1)
