#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite('v0.18.0.0 CP3.75 Candidate10 coastal contour topology')
raster = read(SOURCE / 'Terrain' / 'AERISTerrainGpuTileRasterizer.cs')
policy = read(SOURCE / 'Terrain' / 'AERISTerrainCoastlinePolicy.cs')
renderer = read(SOURCE / 'Terrain' / 'AERISTerrainGpuTileRenderer.cs')
version = read(ROOT / 'GameData' / 'AERISFlightControl' / 'AERISFlightControl.version')

start = raster.index('static float[] BuildContours')
end = raster.index('static void AddCrossing', start)
contours = raster[start:end]

suite.check('AppendTriangleContours(output, tile, interval' in contours,
            'contours are generated from terrain-mesh triangles')
suite.check(contours.count('AppendTriangleContours(output, tile, interval') == 2,
            'each coarse cell uses the same two triangles as the surface mesh')
suite.check('HighDensityBoundaryCrossesParentCell(tile, row, column)' in contours,
            'HD coastline-crossed parent cells quarantine coarse contours')
suite.check('static bool HighDensityBoundaryCrossesParentCell' in contours,
            'HD parent-cell boundary classifier exists in contour path')
suite.check('pointCount >= 4' not in contours,
            'old four-crossing square contour pairing is absent')
suite.check('if (pointCount >= 4)' not in contours,
            'ambiguous saddle-cell 4-point branch cannot regress')
suite.check('tile.Flags[i0] == 2' in contours and
            'tile.Flags[i1] == 2' in contours and 'tile.Flags[i2] == 2' in contours,
            'triangle contours remain land-only')

# Candidate9 authority must remain intact.
suite.check('return CrossingFraction(water0, water1);' in policy,
            'coastline and sparse fill retain one classified-edge authority')
suite.check('frontColourMode != effectiveMode' in renderer and
            'frontColourPreset != currentPreset' in renderer,
            'FRONT colour-mode/preset mismatch still forces refresh')
suite.check('new Color32(70, 235, 70, 255)' in renderer and
            'new Color32(12, 72, 24, 255)' in renderer,
            'Candidate9 High Contrast REL green bands remain intact')
suite.check('COASTAL CONTOUR TOPOLOGY CANDIDATE 10' in version.upper(),
            'package identity is Candidate10')

suite.finish()
