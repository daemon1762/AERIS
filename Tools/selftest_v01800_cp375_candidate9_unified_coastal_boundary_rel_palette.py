#!/usr/bin/env python3
from pathlib import Path
import re,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
failures=0

def read(rel): return (ROOT/rel).read_text(encoding='utf-8')
def check(cond,label,detail=''):
    global failures
    if cond: print('[PASS] '+label)
    else:
        failures+=1
        print('[FAIL] '+label+(' :: '+detail if detail else ''))

print('[AERIS] CP3.75 Candidate9 unified coastal boundary / REL palette static test')
version=read('Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
avc=read('GameData/AERISFlightControl/AERISFlightControl.version')
renderer=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
raster=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs')
policy=read('Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs')
extractor=read('Source/AERISFlightControl/Terrain/AERISTerrainCoastlineExtractor.cs')

check('UNIFIED COASTAL BOUNDARY REL PALETTE AUTHORITY CANDIDATE 9' in version,
      'Candidate9 generated identity')
check('Unified Coastal Boundary REL Palette Authority Candidate 9' in avc,
      'Candidate9 AVC identity')

# Candidate8 architecture remains the foundation.
check('int resolution = tile == null ? 0 : tile.Resolution;' in raster,
      'base render-ready resolution remains low-resolution tile authority')
check('resolution = highDensityBoundary' not in raster,
      'whole-tile 129 surface promotion remains prohibited')
check('BuildSparseCoastalCorrections' in raster,
      'sparse coastal correction remains active')
check('HighDensityResolution = 129' in extractor,
      '129x129 coastline classification authority retained')
check('MaximumSparseCorrectionParentCells = 256' in raster,
      'documented 256-parent sparse safety rail restored')

# Candidate9 boundary authority: both overloads must resolve the same classified edge.
m=re.search(r'internal static float CrossingFraction\(bool water0, bool water1,\s*float elevation0Meters, float elevation1Meters\)\s*\{(?P<body>.*?)\n\s*\}',policy,re.S)
check(m is not None,'elevation-aware boundary overload exists')
if m:
    body=m.group('body')
    check('return CrossingFraction(water0, water1);' in body,
          'HD line and sparse fill share classified-edge crossing authority')
    check('WaterElevationThresholdMeters - elevation0Meters' not in body,
          'independent elevation interpolation no longer shifts presentation boundary')
check('AERISTerrainCoastlinePolicy.CrossingFraction' in extractor and
      'AERISTerrainCoastlinePolicy.CrossingFraction' in raster,
      'coastline extractor and sparse clipper both call shared boundary policy')

# REL palette is safety-semantic in every normal preset.
check('new Color32(0, 190, 255, 255)' not in renderer,
      'HighContrast REL cyan safety-band regression removed')
check('new Color32(70, 235, 70, 255)' in renderer,
      'HighContrast REL near-safe band is high-contrast green')
check('new Color32(12, 72, 24, 255)' in renderer,
      'HighContrast REL distant-safe band remains visible dark green')
check('new Color32(224, 31, 20, 255)' in renderer and
      'new Color32(235, 184, 20, 255)' in renderer,
      'REL red/yellow danger and caution semantics retained')

# FRONT presentation must not display stale palette/mode texture after AUTO or preset change.
check('frontColourMode' in renderer and 'frontColourPreset' in renderer,
      'FRONT stores committed colour authority')
check('colourRefreshRequired = frontColourMode != effectiveMode' in renderer and
      'frontColourPreset != currentPreset' in renderer,
      'mode/preset transition explicitly requests BACK refresh')
check('bool colourCompatible = frontColourMode == effectiveMode' in renderer,
      'direct FRONT requires current colour authority')
check('!present && colourCompatible &&' in renderer and
      'CanPresentLatchedFront' in renderer,
      'latched FRONT cannot preserve stale palette')
check(renderer.count('frontColourMode = effectiveMode;') >= 2 and
      renderer.count('frontColourPreset = currentPreset;') >= 2,
      'normal and recovery swaps commit current colour authority')
check('frontColourMode = (AERISTerrainDisplayMode)(-1);' in renderer and
      'frontColourPreset = (AERISTerrainColourPreset)(-1);' in renderer,
      'FRONT reset clears colour authority')

# Candidate9 is a correction, not a Candidate7 regression.
buildmesh=raster[raster.find('static AERISTerrainGpuTileRasterResult BuildMesh'):
                 raster.find('struct CorrectionPoint')]
check('HighDensityCoastalFlags[index]' not in buildmesh,
      'main base surface never indexes HD mask directly')
check('(float[])tile.HighDensityCoastlineSegments.Clone()' in raster,
      '129-derived coastline vector remains presentation authority')

if failures:
    print('[AERIS] CP3.75 Candidate9 static authority FAIL: %d failure(s)' % failures)
    raise SystemExit(1)
print('[AERIS] CP3.75 Candidate9 unified coastal boundary / REL palette authority PASS')
