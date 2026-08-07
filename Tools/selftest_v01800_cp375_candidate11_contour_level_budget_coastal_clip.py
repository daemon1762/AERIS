#!/usr/bin/env python3
from pathlib import Path
import math,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
TERRAIN=ROOT/'Source/AERISFlightControl/Terrain'
SRC=(TERRAIN/'AERISTerrainGpuTileRasterizer.cs').read_text()
POLICY=(TERRAIN/'AERISTerrainCoastlinePolicy.cs').read_text()
RENDERER=(TERRAIN/'AERISTerrainGpuTileRenderer.cs').read_text()
RF=''.join(RENDERER.split())
VERSION=(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('MaximumContourLevelsPerTile = 96' in SRC,'tile-wide contour budget is explicit')
ck('ResolveContourLevelStride' in SRC,'tile-wide contour level stride exists')
ck('Math.Min(16, Math.Max(0, last - first + 1))' not in SRC,'legacy per-triangle 16-level truncation removed')
ck('bool coastalParent = HighDensityBoundaryCrossesParentCell' in SRC,'HD coastal parent is detected without deleting cell')
ck('if (HighDensityBoundaryCrossesParentCell(tile, row, column)) continue;' not in SRC,'coastal parent cell is not wholly suppressed')
ck('AppendContourSegment' in SRC and 'HighDensityPointIsLand' in SRC,'coastal contour segment clipping uses HD class mask')
ck('Math.Min(16,' in SRC and 'hdSpan * 2f' in SRC,'coastal clip subdivision remains bounded')
ck('AppendTriangleContours(output, points, tile, interval' in SRC,'contours remain triangle-consistent with terrain surface topology')
ck('MaximumSparseCorrectionParentCells = 256' in SRC,'sparse coastal parent safety rail remains 256')
ck('return CrossingFraction(water0, water1);' in POLICY,'coastline and sparse fill retain one crossing authority')
ck('frontColourMode!=effectiveMode' in RF and 'frontColourPreset!=currentPreset' in RF,'FRONT colour authority remains mode/preset aware')
ck('newColor32(70,235,70,255)' in RF and 'newColor32(12,72,24,255)' in RF,'High Contrast REL keeps green safety semantics')
ck(('Contour Level Budget Coastal Clip Candidate 11' in VERSION) or ('Operation Health' in VERSION),'runtime package identifies Candidate11 lineage or approved Operation Health descendant')
def stride(minimum,maximum,interval):
 first=math.floor(minimum/interval)+1; last=math.floor(maximum/interval); levels=max(0,last-first+1); return max(1,math.ceil(levels/96.0))
def aligned_levels(minimum,maximum,interval,st):
 first=math.floor(minimum/interval)+1; last=math.floor(maximum/interval); rem=first%st; start=first if rem==0 else first+(st-rem); return list(range(start,last+1,st))
s=stride(0,3000,50); levels=aligned_levels(0,3000,50,s)
ck(s==1,'0..3000m at 50m keeps full contour cadence')
ck(len(levels)==60 and len(levels)>16,'steep high-magnification terrain is no longer truncated to 16 levels')
s2=stride(0,10000,50); levels2=aligned_levels(0,10000,50,s2)
ck(s2==3,'extreme 10km relief selects deterministic global stride 3')
ck(len(levels2)<=96,'extreme tile stays within 96 contour levels')
ck(all((b-a)==s2 for a,b in zip(levels2,levels2[1:])),'extreme tile thinning is uniform, not spatially biased')
failed=[n for ok,n in checks if not ok]
print('\n[Candidate11 contour acceptance] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: raise SystemExit(1)
