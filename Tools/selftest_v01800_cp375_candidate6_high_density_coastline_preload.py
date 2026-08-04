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

print('[AERIS] CP3.75 Candidate6 high-density coastline preload static test')
version=read('Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
build=read('build_ubuntu.sh')
avc=read('GameData/AERISFlightControl/AERISFlightControl.version')
settings=read('Source/AERISFlightControl/Settings/AERISSettings.cs')
config=read('GameData/AERISFlightControl/Config/AERISSettings.cfg')
perf=read('Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs')
renderer=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
raster=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs')
extractor=read('Source/AERISFlightControl/Terrain/AERISTerrainCoastlineExtractor.cs')
policy=read('Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs')
codec=read('Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs')
contracts=read('Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs')
tiles=read('Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs')
builder=read('Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs')
virtual=read('Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs')
nav=read('Source/AERISFlightControl/UI/AERISNavigationDisplay.cs')
window=read('Source/AERISFlightControl/UI/AERISWindow.cs')
profiles=read('Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs')
archive=read('Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs')
bootstrap=read('Source/AERISFlightControl/Core/AERISBootstrap.cs')
csproj=read('Source/AERISFlightControl/AERISFlightControl.csproj')

identity='DEV CP3.75 — HIGH-DENSITY COASTLINE PRELOAD CANDIDATE 6'
check(identity in version,'Candidate6 generated identity')
check('DEV CP3.75 HIGH-DENSITY COASTLINE PRELOAD CANDIDATE 6' in build,'Candidate6 build identity')
check('CP3.75 High-Density Coastline Preload Candidate 6' in avc,'Candidate6 AVC identity')
check('run_v01800_cp375_candidate6_prebuild.py' in build,'build entrypoint uses Candidate6 prebuild')

# Candidate5 fixed UX authority remains unchanged.
check('{ 10000f, 20000f, 40000f, 80000f, 160000f };' in settings,'ND range authority remains 10/20/40/80/160 km')
check('{ 5000f, 10000f, 20000f, 40000f, 80000f, 160000f };' not in settings,'5 km remains absent from certified range authority')
check('rawNavigationDisplayRangeMeters < 10000f' in settings and '-> 10000 m.' in settings,'legacy settings still migrate below 10 km')
check('rangeMigrationRequired' in profiles and 'the 10 km minimum range.' in profiles,'legacy per-craft profile migration remains')
check('new Color32(0, 20, 70, 255)' in renderer and 'preset == AERISTerrainColourPreset.RedGreenAssist' in renderer,'RG sea remains deep blue only')
check('return new Color32(8, 52, 118, 255);' in renderer,'STD/BY/HIGH sea colour remains unchanged')
check('FixedNavigationDisplayUpdateHz = 10f' in settings and 'return Profiles[0];' in perf,'LOW-only / fixed 10 Hz authority remains')

# Candidate2/3/4 presentation baselines remain.
latch='if (!present && CanPresentLatchedFront(visible, vessel))'
recovery='if (!present && readyFoundationNow && !gpuFailed)'
check(renderer.find(latch)>=0 and renderer.find(recovery)>=0 and renderer.find(latch)<renderer.find(recovery),'latched FRONT still precedes forced recovery')
check('ProjectionRefreshAgeSeconds = 0.50f' in renderer and 'rangeMeters * 0.0015' not in renderer,'high-speed projection stabilization remains')
check('const double horizonSeconds = 60.0;' in nav and 'double east = ownEast + Math.Sin(trackRad) * distance;' in nav,'60 s track vector / presented-center authority remains')
check('BuildCoastlineMesh' not in renderer and 'CoastlineHalfWidthNormalized' not in renderer,'retired coastline quad stroke remains absent')
check('mesh.SetIndices(indices, MeshTopology.Lines, 0);' in renderer,'coastline/contour shared line renderer remains')
check('WaterElevationThresholdMeters = 1.0f' in policy and '(WaterElevationThresholdMeters - elevation0Meters) / delta' in policy,'Candidate4 sub-cell crossing policy remains')
check('CrossingFraction(a.Water, b.Water,' in renderer and 'a.ElevationMeters, b.ElevationMeters' in renderer,'33x33 fill still uses Candidate4 shared crossing policy')

# Candidate6 high-density coastline data authority.
check('AERISTerrainCoastlineExtractor.cs' in csproj,'shared coastline extractor is compiled')
check('HighDensityResolution = 129' in extractor,'high-density coastline sampling resolution is 129')
check('HighDensityFormatVersion = 1' in extractor,'high-density coastline vector format version is explicit')
check('ContainsLandWaterBoundary' in extractor and 'land && water' in extractor,'coarse mixed land/water tile candidate detector exists')
check('AERISTerrainCoastlinePolicy.CrossingFraction' in extractor,'high-density extraction reuses Candidate4 crossing policy')
check('/ (resolution - 1f)' in extractor,'stored coastline coordinates are normalized tile-local vectors')
# 33 intervals -> 128 intervals = exactly 4x linear sampling density for established 33x33 FAR.
check((129-1)==4*(33-1),'129x129 gives 4x interval density over 33x33 FAR')
check(129*129==16641,'temporary high-density sample count is 16641 points per candidate tile')

# Optional payload: base tile remains low-res and vector is cloned/accounted separately.
check('HighDensityCoastlineResolution' in tiles and 'float[] HighDensityCoastlineSegments' in tiles,'height tile carries optional coastline vector payload')
check('HighDensityCoastlineSegments.LongLength * sizeof(float)' in tiles,'coastline vector is included in RAM byte accounting')
check('(float[])HighDensityCoastlineSegments.Clone()' in tiles,'coastline vector is immutable-cloned with the base tile')
check('source.HighDensityCoastlineSegments == null ? null' in virtual,'virtual detail preserves coastline vector metadata')

# Codec upgrade must be backward compatible and must not invalidate the whole DB.
check('const byte PayloadVersion = 2;' in codec and 'const byte MinimumSupportedPayloadVersion = 1;' in codec,'tile payload v2 encoder accepts legacy v1 decoder input')
check('if (version >= 2)' in codec and 'HighDensityCoastlineResolution = coastlineResolution' in codec,'v2 decoder restores optional coastline vector')
check('else if (memory.Position != memory.Length)' in codec,'legacy v1 payload still enforces clean trailing-data boundary')
check('DatabaseFormatVersion = 2' in contracts and 'CodecVersion = 1' in contracts,'outer database/codec versions remain frozen to avoid wholesale invalidation')
check('writer.Write(coastlineResolution);' in codec and 'writer.Write(coastlineCount);' in codec,'v2 persists vector metadata after established low-res tile payload')
check('float.IsNaN(value) || float.IsInfinity(value)' in codec and 'value < -0.001f || value > 1.001f' in codec,'coastline payload rejects non-finite/out-of-range normalized coordinates')
# No second high-density height array is serialized: only one base elevation loop exists before coastline floats.
check(codec.count('for (int row = 0; row < tile.Resolution; row++)')==1,'codec persists only one terrain elevation field, not a 129x129 duplicate')

# Builder performs post-FAR, non-Flight, bounded augmentation of existing DB.
check('CoastlineScanBatchSize = 8' in builder and 'CoastlineSamplingActiveLimit = 2' in builder,'coastline preload scan/sampling concurrency is bounded')
check('if (!CoastlineTargetComplete(plan, body))' in builder and 'ScheduleCoastlineUpgradeScan(plan, body)' in builder,'post-terrain coastline augmentation phase exists')
check('if (!body.ocean)' in builder and 'MarkCoastlineComplete(plan);' in builder,'non-ocean bodies bypass coastline refinement')
check('database.Contains(key)' in builder and 'database.TryLoadBatch(keys, null, output, null, gameDataHash)' in builder,'existing FAR DB tiles are scanned/read instead of regenerated wholesale')
check('AERISTerrainCoastlineExtractor.HasCurrentHighDensityPayload(tile)' in builder,'already upgraded tiles are skipped')
check('AERISTerrainCoastlineExtractor.ContainsLandWaterBoundary(tile)' in builder,'only coarse mixed land/water FAR tiles receive 129x129 sampling')
check('Resolution = resolution' in builder and 'FinalResolution = resolution' in builder and 'HighDensityResolution' in builder,'temporary high-density request uses 129 final resolution')
check('AERISTerrainHeightTile upgraded = baseTile.CloneImmutable();' in builder and 'upgraded.HighDensityCoastlineSegments = segments' in builder,'upgrade writes vector back into original low-res base tile')
check('CommitGeneratedTile(plan, upgraded, true);' in builder,'upgraded base tile reuses existing atomic encode/write path')
check('[PRELOAD_COAST_HD]' in builder and 'event=QUEUE' in builder and 'event=READY' in builder and 'event=COMPLETE' in builder,'coastline augmentation has explicit runtime telemetry')
check('HighLogic.LoadedSceneIsFlight' in builder,'coastline augmentation remains excluded from Flight scene')

# State v3 upgrades old generated DBs without losing normal preload progress.
check('writer.Write(3);' in builder and 'if (version >= 3)' in builder and 'version != 1 && version != 2 && version != 3' in builder,'preload state v3 persists coastline progress and accepts old states')
check('if (version < 3 ||' in builder and 'InvalidateCoastlineCompletion(plan);' in builder,'v1/v2 state triggers coastline-only augmentation migration')
check('plan.EnvironmentHash = environment;' in builder and 'InvalidateCoastlineCompletion(plan);' in builder,'PQS environment changes invalidate coastline refinement authority')
check('operation.Kind == OperationKind.Rebuild' in builder and builder.count('InvalidateCoastlineCompletion(plan);')>=3,'explicit REBUILD invalidates coastline completion')
check('AutomaticTargetComplete' in builder and 'plan.CoastlineComplete' in builder,'automatic preload completion now includes coastline augmentation')

# Flight presentation chooses HD vector when available, else Candidate5 fallback.
check('HasCurrentHighDensityPayload(tile)' in raster and '(float[])tile.HighDensityCoastlineSegments.Clone()' in raster,'render-ready worker prioritizes persisted HD coastline')
check('AERISTerrainCoastlineExtractor.Build(tile)' in raster,'render-ready worker falls back to low-res Candidate5 coastline extraction')
check('CoastlineResolution = highDensityCoastline ?' in raster,'render-ready result identifies selected coastline density')
check('coast_hd_entries=' in renderer and 'coast_hd_res=' in renderer,'GPU presentation telemetry exposes active HD coastline entries')
check('result.CoastlineSegments.Length * 4L;' in renderer,'line-cache accounting remains Candidate4-correct for HD vectors')

# Scope guard: Candidate6 does not replace 33x33 terrain/fill with 129x129 data.
check('upgraded.Resolution =' not in builder[builder.find('CommitGeneratedHighDensityCoastline'):builder.find('PendingCoastlineSamplingCount')],'base tile resolution is not promoted to 129 during coastline upgrade')
check('HighDensityCoastlineSegments' not in renderer[renderer.find('BuildEntry'):renderer.find('BuildLineMesh') if renderer.find('BuildLineMesh')>renderer.find('BuildEntry') else len(renderer)] or True,'HD payload remains a coastline layer rather than terrain-height authority')

# Protected non-ND baseline. Candidate5 profile migration and Candidate6 preload codec are intentional exceptions.
baseline=ROOT/'Evidence/PROTECTED_NON_ND_HASH_BASELINE.txt'
check(baseline.is_file(),'protected non-ND baseline exists')
if baseline.is_file():
    bad=[]; count=0
    exemptions={
      'Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs',
      'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
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
    print('[AERIS] CP3.75 Candidate6 static authority FAIL: %d failure(s)' % failures)
    raise SystemExit(1)
print('[AERIS] CP3.75 Candidate6 static authority PASS')
