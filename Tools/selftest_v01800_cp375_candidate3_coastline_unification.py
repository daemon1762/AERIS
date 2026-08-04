#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Source/AERISFlightControl'

failures=0

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def read(rel): return (ROOT/rel).read_text(encoding='utf-8')
def check(cond,label,detail=''):
    global failures
    if cond: print('[PASS] '+label)
    else:
        failures+=1
        print('[FAIL] '+label+(' :: '+detail if detail else ''))

print('[AERIS] CP3.75 Candidate3 coastline/contour path unification static test')

version=read('Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs')
build=read('build_ubuntu.sh')
avc=read('GameData/AERISFlightControl/AERISFlightControl.version')
settings=read('Source/AERISFlightControl/Settings/AERISSettings.cs')
config=read('GameData/AERISFlightControl/Config/AERISSettings.cfg')
perf=read('Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs')
renderer=read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
nav=read('Source/AERISFlightControl/UI/AERISNavigationDisplay.cs')
window=read('Source/AERISFlightControl/UI/AERISWindow.cs')
archive=read('Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs')
bootstrap=read('Source/AERISFlightControl/Core/AERISBootstrap.cs')

identity='DEV CP3.75 — COASTLINE / CONTOUR PATH UNIFICATION CANDIDATE 3'
check(identity in version,'Candidate3 generated identity')
check('DEV CP3.75 COASTLINE CONTOUR PATH UNIFICATION CANDIDATE 3' in build,
      'Candidate3 build identity')
check('CP3.75 Coastline Contour Path Unification Candidate 3' in avc,
      'Candidate3 AVC identity')
check('run_v01800_cp375_candidate3_prebuild.py' in build,
      'build entrypoint uses Candidate3 prebuild')

# Settings consolidation.
check('CurrentTerrainQualityModelRevision = 3' in settings,
      'terrain quality policy revision advanced')
check('FixedNavigationDisplayUpdateHz = 10f' in settings,
      'fixed ND presentation cadence authority is 10 Hz')
check('settings.TerrainQualityMode = AERISTerrainQualityMode.Low;' in settings and
      'settings.NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;' in settings,
      'loaded legacy settings are normalized to LOW / 10 Hz')
check('settings.TerrainLandRuntimeQualityEnabled = false;' in settings,
      'separate LAND terrain-quality capability disabled')
check('terrainQualityModelRevision = 3' in config and 'terrainQualityMode = Low' in config and
      'navigationDisplayUpdateMode = Fps10' in config,
      'default CFG is LOW / 10 Hz')
check('LOW  (LOCKED)' in window,
      'terrain quality UI retains disabled LOW button')
check('DrawNavigationDisplayUpdateSelector' not in window and 'new string[]{"AUTO","10","20","30","45","60"}' not in window,
      'ND update selector and legacy choices removed from UI')
check('return Profiles[0];' in perf,
      'runtime terrain profile is Golden LOW only')
check('return AERISSettings.FixedNavigationDisplayUpdateHz;' in perf,
      'performance controller resolves fixed 10 Hz')
check('get { return false; }' in perf and 'return 2f;' in perf,
      'AUTO adaptation is retired and tile planning is separately fixed')

# High-speed presentation recovery ordering and tolerance.
latch_marker='if (!present && CanPresentLatchedFront(visible, vessel))'
recovery_marker='if (!present && readyFoundationNow && !gpuFailed)'
check(renderer.find(latch_marker) >= 0 and renderer.find(recovery_marker) >= 0 and
      renderer.find(latch_marker) < renderer.find(recovery_marker),
      'complete latched FRONT is presented before last-resort forced recovery')
check('ProjectionRefreshAgeSeconds = 0.50f' in renderer,
      'projection refresh age bounds high-speed map-center lag')
check('rangeMeters * 0.0015' not in renderer and 'Math.Max(25.0' not in renderer,
      'Candidate1 speed-amplifying exact-center tolerance removed')
check('ProjectionRefreshDistanceMeters(rangeMeters)' in renderer,
      'direct compatibility and scheduled refresh share one distance authority')
check('nextBackRefreshRealtime = Time.realtimeSinceStartup + 0.10f;' in renderer,
      'BACK retry cadence is bounded to no faster than 10 Hz')
check('GUI.matrix' not in renderer[renderer.find('CP3.75 Candidate 2 presentation continuity'):renderer.find('bool RenderBackBuffer')],
      'Candidate2 continuity does not reintroduce temporal GUI.matrix warp')

# Track-vector geometry.
check('ToLocalMeters(vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg,' in nav and
      'double east = ownEast + Math.Sin(trackRad) * distance;' in nav and
      'double north = ownNorth + Math.Cos(trackRad) * distance;' in nav,
      'track-vector endpoint uses presented-map-center ownship offset')
check('const double horizonSeconds = 60.0;' in nav and
      'if (tickDistance > distance + 1.0) continue;' in nav,
      'track-vector horizon is stable 60 s with range clipping')
# Formula-level regression for the HHC4 stress point.
range_m=160000.0; speed=2100.0; horizon=60.0
distance=min(range_m*0.42,speed*horizon)
check(speed*30.0 <= distance < speed*45.0,
      '160 km / 2100 m/s retains 30 s vector tick and clips 45 s tick',
      'distance=%.1f m' % distance)

# LAND localizer/funnel geometry.
check('double thresholdEast = ownEast + observation.ThresholdEastMeters;' in nav and
      'double oppositeEast = ownEast + observation.OppositeEastMeters;' in nav,
      'LAND runway endpoints converted from ownship-relative to presented-center-relative')
check('double farEast = thresholdEast - unitEast * captureDistance;' in nav and
      'plot, anchorV, out farLeft' in nav and 'plot, anchorV, out farRight' in nav,
      'LAND localizer funnel stays on presented projection authority')

# Candidate3 coastline presentation unification.
check('BuildCoastlineMesh' not in renderer and 'CoastlineHalfWidthNormalized' not in renderer,
      'legacy per-segment coastline quad/stroke expansion removed')
coast_call='Mesh coastlineMesh = BuildLineMesh("AERIS_TERRAIN_COAST_" +'
check(coast_call in renderer and 'result.CoastlineSegments' in renderer and
      'new Color32(185, 225, 255, 245), out coastlineSource' in renderer,
      'coastline uses the same BuildLineMesh path as contours')
check('mesh.SetIndices(indices, MeshTopology.Lines, 0);' in renderer,
      'shared line path uses MeshTopology.Lines')
check('BuildCoastlines(tile)' in read('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'),
      'Golden coastline extraction remains unchanged')
check('CoastlineMesh' in renderer and 'Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);' in renderer,
      'coastline remains a first-class GPU presentation layer')
check('CPU final terrain presentation' not in window,
      'no UI path advertises CPU final terrain presentation')

# Protected non-ND baseline.
baseline=ROOT/'Evidence/PROTECTED_NON_ND_HASH_BASELINE.txt'
check(baseline.is_file(),'protected non-ND baseline exists')
if baseline.is_file():
    bad=[]; count=0
    for line in baseline.read_text(encoding='utf-8').splitlines():
        m=re.match(r'^([0-9a-f]{64})  (.+)$',line)
        if not m: continue
        want,rel=m.groups(); count+=1
        path=ROOT/rel
        got=sha(path) if path.is_file() else 'MISSING'
        if got!=want: bad.append(rel)
    check(count>=100,'protected non-ND baseline has expected coverage',str(count))
    check(not bad,'protected non-ND files remain exact',', '.join(bad[:10]))

# Later non-ND FDR/CVR retention remains.
check('FlightDataArchiveLimit = 10' in settings and 'NormalizeFlightDataArchiveLimit' in settings,
      'FDR/CVR retention settings preserved')
check('AERISFlightDataArchive.ConfigureRetention(settings.FlightDataArchiveLimit)' in bootstrap,
      'FDR/CVR retention bootstrap preserved')
check('VerifiedMarkerSuffix' in archive and 'PruneVerifiedArchives' in archive,
      'verified-archive pruning implementation preserved')

if failures:
    print('[AERIS] CP3.75 Candidate3 static authority FAIL: %d failure(s)' % failures)
    raise SystemExit(1)
print('[AERIS] CP3.75 Candidate3 static authority PASS')
