#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
failures=0

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def read(rel): return (ROOT/rel).read_text(encoding='utf-8')
def check(cond,label,detail=''):
    global failures
    if cond: print('[PASS] '+label)
    else:
        failures+=1
        print('[FAIL] '+label+(' :: '+detail if detail else ''))

print('[AERIS] CP3.75 Candidate8 sparse coastal-correction static test')
version=read('Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
build=read('build_ubuntu.sh')
avc=read('GameData/AERISFlightControl/AERISFlightControl.version')
settings=read('Source/AERISFlightControl/Settings/AERISSettings.cs')
perf=read('Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs')
renderer=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
raster=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs')
extractor=read('Source/AERISFlightControl/Terrain/AERISTerrainCoastlineExtractor.cs')
codec=read('Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs')
contracts=read('Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs')
tiles=read('Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs')
builder=read('Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs')
tilesystem=read('Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs')
nav=read('Source/AERISFlightControl/UI/AERISNavigationDisplay.cs')
archive=read('Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs')
bootstrap=read('Source/AERISFlightControl/Core/AERISBootstrap.cs')

identity='DEV CP3.75 — SPARSE COASTAL CORRECTION CANDIDATE 8'
check(identity in version,'Candidate8 generated identity')
check('DEV CP3.75 SPARSE COASTAL CORRECTION CANDIDATE 8' in build,'Candidate8 build identity')
check('CP3.75 Sparse Coastal Correction Candidate 8' in avc,'Candidate8 AVC identity')
check('run_v01800_cp375_candidate8_prebuild.py' in build,'build entrypoint uses Candidate8 prebuild')

# Candidate5/Candidate2 stable UX/presentation remains.
check('{ 10000f, 20000f, 40000f, 80000f, 160000f };' in settings,'ND range authority remains 10/20/40/80/160 km')
check('{ 5000f, 10000f, 20000f, 40000f, 80000f, 160000f };' not in settings,'5 km remains absent')
check('FixedNavigationDisplayUpdateHz = 10f' in settings and 'return Profiles[0];' in perf,'LOW-only / fixed 10 Hz authority remains')
check('new Color32(0, 20, 70, 255)' in renderer,'RG deep-blue sea remains')
check('ProjectionRefreshAgeSeconds = 0.50f' in renderer and 'rangeMeters * 0.0015' not in renderer,'high-speed projection stabilization remains')
check('const double horizonSeconds = 60.0;' in nav,'60 s prediction horizon remains')
check('BuildCoastlineMesh' not in renderer and 'MeshTopology.Lines' in renderer,'uniform line coastline renderer remains')

# Candidate7 preload authority and percentage are retained.
check('DatabaseFormatVersion = 3' in contracts and 'TerrainPreloadDatabaseV3' in tilesystem,'Candidate7 v3 preload authority retained')
check('Version = 3' in tiles and 'AERIS_TERRAIN_TILE_V3' in tiles,'terrain tile format remains v3')
check('const byte PayloadVersion = 3;' in codec and 'const byte MinimumSupportedPayloadVersion = 3;' in codec,'coastal mask payload remains v3')
check('HighDensityResolution = 129' in extractor and 'HighDensityFormatVersion = 2' in extractor,'129x129 coastal authority retained')
check('byte[] HighDensityCoastalFlags' in tiles and 'HighDensityCoastalFlags = coastalFlags' in codec,'129 coastal class mask remains persisted')
check('CombinedPreloadCoverage' in builder and 'CoastlineCoverageRatio' in builder,'terrain+coastal combined progress remains')
check('terrainTargetTiles + (double)coastalUnits' in builder,'coastal work remains in percentage denominator')
check('Math.Min(0.999, ratio)' in builder,'incomplete preload cannot display 100%')

# Candidate8 core: never build a full 129x129 base surface.
check('int resolution = tile == null ? 0 : tile.Resolution;' in raster,'base render-ready resolution comes from low-res tile')
check('tile.HighDensityCoastlineResolution : baseResolution' not in raster,'Candidate7 whole-tile 129 promotion removed')
check('BuildSparseCoastalCorrections' in raster,'sparse coastal correction builder exists')
check('parentWidth = baseResolution - 1' in raster and 'row / factor' in raster and 'column / factor' in raster,'HD crossings collapse to sparse coarse parent cells')
check('if (!parents[pr * parentWidth + pc]) continue;' in raster,'only crossed parent cells emit correction geometry')
check('CoastalLandCorrectionVertices' in raster and 'CoastalWaterCorrectionVertices' in raster,'worker publishes split land/water correction triangles')
check('CoastalCorrectionParentCells' in raster,'worker publishes sparse parent-cell count')
check('MaximumSparseCorrectionParentCells = 64' in raster and 'detectedParents > MaximumSparseCorrectionParentCells' in raster,'pathological coastline cannot expand back to whole-tile HD fill')
check('AERISTerrainCoastlineExtractor.HasCurrentHighDensityPayload(tile)' in raster,'sparse correction activates only with complete HD payload')
check('(float[])tile.HighDensityCoastlineSegments.Clone()' in raster,'HD coastline vector still drives line geometry')
check('BuildContours(tile,' in raster,'contours remain 33x33 height-derived')

# Renderer: base meshes then sparse painter-order correction, not a full HD replacement.
check('CoastalLandCorrectionMesh' in renderer and 'CoastalWaterCorrectionMesh' in renderer,'renderer carries sparse correction meshes')
check('BuildTriangleListMesh' in renderer,'correction triangles use direct compact triangle-list meshes')
check('Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)' in renderer and 'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)' in renderer and 'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)' in renderer,'correction overlays base fill in painter order')
check(renderer.find('Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)') < renderer.find('Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)') < renderer.find('Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)'),'draw order is base -> water fix -> land fix')
check('ProjectMesh(entry.CoastalLandCorrectionMesh' in renderer and 'ProjectMesh(entry.CoastalWaterCorrectionMesh' in renderer,'correction geometry shares exact map projection authority')
check('coast_sparse_entries=' in renderer and 'coast_sparse_parents=' in renderer,'runtime telemetry exposes sparse correction cost')
check('UnityEngine.Rendering.IndexFormat.UInt32' in renderer,'large correction meshes remain index-safe')

# Candidate7 full-HD fill signature must be absent from BuildMesh main path.
buildmesh=raster[raster.find('static AERISTerrainGpuTileRasterResult BuildMesh'):raster.find('struct CorrectionPoint')]
check('HighDensityCoastalFlags[index]' not in buildmesh,'main surface grid never indexes the 129 mask directly')
check('resolution = highDensityBoundary' not in buildmesh,'main surface resolution is never switched to 129')

# Protected non-ND baseline remains exact except intentional historical exemptions.
baseline=ROOT/'Evidence/PROTECTED_NON_ND_HASH_BASELINE.txt'
check(baseline.is_file(),'protected non-ND baseline exists')
if baseline.is_file():
    bad=[]; count=0
    exemptions={
      'Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs',
      'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
      'Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs',
    }
    for line in baseline.read_text(encoding='utf-8').splitlines():
        m=re.match(r'^([0-9a-f]{64})  (.+)$',line)
        if not m: continue
        want,rel=m.groups(); count+=1
        if rel in exemptions: continue
        path=ROOT/rel; got=sha(path) if path.is_file() else 'MISSING'
        if got!=want: bad.append(rel)
    check(count>=100,'protected non-ND baseline has expected coverage',str(count))
    check(not bad,'protected non-ND files remain exact',', '.join(bad[:10]))

check('FlightDataArchiveLimit = 10' in settings and 'NormalizeFlightDataArchiveLimit' in settings,'FDR/CVR retention settings preserved')
check('AERISFlightDataArchive.ConfigureRetention(settings.FlightDataArchiveLimit)' in bootstrap,'FDR/CVR bootstrap preserved')
check('VerifiedMarkerSuffix' in archive and 'PruneVerifiedArchives' in archive,'verified archive pruning preserved')

if failures:
    print('[AERIS] CP3.75 Candidate8 static authority FAIL: %d failure(s)' % failures)
    raise SystemExit(1)
print('[AERIS] CP3.75 Candidate8 sparse coastal-correction authority PASS')
