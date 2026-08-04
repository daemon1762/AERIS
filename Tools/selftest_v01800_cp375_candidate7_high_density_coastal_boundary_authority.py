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

print('[AERIS] CP3.75 Candidate7 high-density coastal-boundary authority static test')
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
virtual=read('Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs')
tilesystem=read('Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs')
nav=read('Source/AERISFlightControl/UI/AERISNavigationDisplay.cs')
profiles=read('Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs')
archive=read('Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs')
bootstrap=read('Source/AERISFlightControl/Core/AERISBootstrap.cs')

identity='DEV CP3.75 — HIGH-DENSITY COASTAL BOUNDARY AUTHORITY CANDIDATE 7'
check(identity in version,'Candidate7 generated identity')
check('DEV CP3.75 HIGH-DENSITY COASTAL BOUNDARY AUTHORITY CANDIDATE 7' in build,'Candidate7 build identity')
check('CP3.75 High-Density Coastal Boundary Authority Candidate 7' in avc,'Candidate7 AVC identity')
check('run_v01800_cp375_candidate7_prebuild.py' in build,'build entrypoint uses Candidate7 prebuild')

# Candidate5 UX authority remains fixed.
check('{ 10000f, 20000f, 40000f, 80000f, 160000f };' in settings,'ND range authority remains 10/20/40/80/160 km')
check('{ 5000f, 10000f, 20000f, 40000f, 80000f, 160000f };' not in settings,'5 km remains absent')
check('FixedNavigationDisplayUpdateHz = 10f' in settings and 'return Profiles[0];' in perf,'LOW-only / fixed 10 Hz authority remains')
check('new Color32(0, 20, 70, 255)' in renderer,'RG deep-blue sea remains')

# Candidate2-4 stabilized presentation stays intact.
check('ProjectionRefreshAgeSeconds = 0.50f' in renderer and 'rangeMeters * 0.0015' not in renderer,'high-speed projection stabilization remains')
check('const double horizonSeconds = 60.0;' in nav,'60 s prediction horizon remains')
check('BuildCoastlineMesh' not in renderer and 'MeshTopology.Lines' in renderer,'uniform line coastline renderer remains')

# Candidate7 intentionally starts a clean preload format/root.
check('DatabaseFormatVersion = 3' in contracts,'preload database format is v3')
check('AERIS_PRELOAD_TERRAIN_MANIFEST_V3' in contracts and 'AERIS_PRELOAD_TERRAIN_CHUNK_V3' in contracts,'manifest/chunk magic are v3')
check('Version = 3' in tiles and 'AERIS_TERRAIN_TILE_V3' in tiles,'terrain tile key/format is v3')
check('TerrainPreloadDatabaseV3' in tilesystem and '"TerrainPreloadDatabase"' not in tilesystem[tilesystem.find('string preloadRoot'):tilesystem.find('preloadDatabase =',tilesystem.find('string preloadRoot'))],'Candidate7 uses a clean v3 preload root')
check('const byte PayloadVersion = 3;' in codec and 'const byte MinimumSupportedPayloadVersion = 3;' in codec,'payload v3 rejects legacy payloads')
check('if (version != 4) return false;' in builder and 'writer.Write(4);' in builder,'preload state v4 rejects legacy state')

# High-density coastal boundary payload.
check('HighDensityResolution = 129' in extractor and 'HighDensityFormatVersion = 2' in extractor,'coastal boundary authority is 129x129 format v2')
check('byte[] HighDensityCoastalFlags' in tiles,'height tile carries 129x129 coastal class mask')
check('HighDensityCoastalFlags.LongLength' in tiles,'coastal class mask is RAM-accounted')
check('(byte[])HighDensityCoastalFlags.Clone()' in tiles and '(byte[])source.HighDensityCoastalFlags.Clone()' in virtual,'coastal class mask is immutable-cloned')
check('coastalFlagCount != coastlineResolution * coastlineResolution' in codec,'codec validates exact coastal mask dimensions')
check('writer.Write(coastalFlagCount);' in codec and 'writer.Write(coastalFlags);' in codec,'codec persists coastal class mask')
check('HighDensityCoastalFlags = coastalFlags' in codec,'decoder restores coastal class mask')
check('tile.HighDensityCoastalFlags.Length == required' in extractor,'HD payload validity requires a complete 129x129 mask')

# Builder: base terrain remains low-res, boundary payload generated from 129 sample.
check('upgraded.HighDensityCoastalFlags = (byte[])sampledTile.Flags.Clone();' in builder,'builder persists sampled 129x129 land/water mask')
segment=builder[builder.find('CommitGeneratedHighDensityCoastline'):builder.find('PendingCoastlineSamplingCount')]
check('upgraded.Resolution =' not in segment,'base tile height resolution is not promoted')
check('sampledTile.Resolution != hdResolution' in builder and 'hdCount = hdResolution * hdResolution' in builder,'builder validates complete 129x129 sample')
check('ContainsLandWaterBoundary(tile)' in builder,'coarse candidate detector bounds expensive HD sampling')

# Progress hard requirement: 100% includes coastal boundary phase.
check('CombinedPreloadCoverage' in builder and 'CoastlineCoverageRatio' in builder,'combined terrain+coast preload progress exists')
check('terrainTargetTiles + (double)coastalUnits' in builder,'coastal work contributes to total percentage denominator')
check('Math.Min(0.999, ratio)' in builder,'incomplete body cannot round to 100.0%')
check('bool ready = AutomaticTargetComplete(plan);' in builder and 'ready ? "READY"' in builder,'READY status requires full automatic target including coast')
check('plan.CoastlineComplete' in builder[builder.find('static bool AutomaticTargetComplete'):builder.find('static void MarkAutomaticComplete')],'automatic completion includes coastal phase')
check('CoastlineProcessedTileIds' in builder and 'MarkCoastlineTileProcessed' in builder,'coastal scan tracks unique processed FAR tiles')
check('CoastlineProcessedCount(plan) >= total' in builder,'coastal phase completes only after all FAR tiles are classified/processed')

# Rendering: fill and coastline use the same 129 classification authority.
check('highDensityBoundary ?' in raster and 'tile.HighDensityCoastalFlags[index]' in raster,'render-ready worker uses HD class mask for fill grid')
check('SampleClassPreservingHeight' in raster,'HD fill interpolates low-res height without promoting elevation authority')
check('BuildContours(tile,' in raster,'contours continue using base low-res height tile')
check('(float[])tile.HighDensityCoastlineSegments.Clone()' in raster,'coastline uses matching HD vector')
check('Resolution = resolution' in raster and 'tile.HighDensityCoastlineResolution : baseResolution' in raster,'coastal fill grid is promoted to 129 only when HD payload is valid')
check('UnityEngine.Rendering.IndexFormat.UInt32' in renderer,'large 129 coastal meshes use 32-bit indices when required')

# Protected non-ND baseline remains exact except intentional settings/profile history.
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
    print('[AERIS] CP3.75 Candidate7 static authority FAIL: %d failure(s)' % failures)
    raise SystemExit(1)
print('[AERIS] CP3.75 Candidate7 static authority PASS')
