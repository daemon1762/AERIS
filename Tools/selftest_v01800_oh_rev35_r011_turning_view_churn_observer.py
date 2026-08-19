#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source' / 'AERISFlightControl'
observer_path = SRC / 'Terrain' / 'AERISR011TurningViewChurnObserver.cs'
renderer_path = SRC / 'Terrain' / 'AERISTerrainGpuTileRenderer.cs'
nav_path = SRC / 'UI' / 'AERISNavigationDisplay.cs'
bootstrap_path = SRC / 'Core' / 'AERISBootstrap.cs'
csproj_path = SRC / 'AERISFlightControl.csproj'
build_path = ROOT / 'build_ubuntu.sh'

checks = []
def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)

ck(observer_path.is_file(), 'R011 observer source exists')
observer = observer_path.read_text() if observer_path.is_file() else ''
renderer = renderer_path.read_text()
nav = nav_path.read_text()
bootstrap = bootstrap_path.read_text()
csproj = csproj_path.read_text()
build = build_path.read_text()

ck('AERISR011TurningViewChurnObserver.cs' in csproj,
   'R011 observer is explicitly compiled by legacy xbuild project')
ck('OH REV3.5 SALBUTAMOL SULFATE R011 TURNING VIEW CHURN OBSERVER' in build,
   'Ubuntu build identity names R011 observer candidate')
ck('const float SampleIntervalSeconds = 0.10f;' in observer,
   'observer samples at nominal 10 Hz')
ck('const float LogIntervalSeconds = 5.0f;' in observer,
   'observer emits bounded five-second summaries')
ck('[OH_REV3_5_R011_TURN_CHURN]' in observer,
   'observer owns a dedicated diagnostic log prefix')

for token, label in [
    ('reason_snapshot=', 'snapshot-invalid reason counter'),
    ('reason_visible=', 'visible-null reason counter'),
    ('reason_terrain_gen=', 'terrain-generation reason counter'),
    ('reason_heading3=', 'three-degree heading reason counter'),
    ('reason_disp2pct=', 'two-percent displacement reason counter'),
    ('front_terrain_gen=', 'FRONT terrain-generation mismatch counter'),
    ('front_view_gen=', 'FRONT view-generation mismatch counter'),
    ('front_content_rev=', 'FRONT content-revision mismatch counter'),
    ('resolve_calls=', 'current ResolveRenderableEntries activity counter'),
    ('requested_clear_est=', 'requested.Clear estimate is explicitly labelled estimate'),
    ('auth_heading005=', 'authoritative turning threshold counter'),
    ('auth_move=', 'authoritative movement threshold counter'),
    ('front_swap=', 'FRONT swap counter')]:
    ck(token in observer, label)

ck('>= 3f' in observer and
   'Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02)' in observer,
   'observer mirrors R010 NeedsContentRefresh motion thresholds')
ck('>= 3f' in renderer and
   'Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02)' in renderer,
   'R010 renderer still owns the authoritative refresh thresholds')

for field in [
    'contentSnapshotValid', 'contentVisible', 'contentTerrainGeneration',
    'contentStyleKey', 'contentCenterLatitudeDeg', 'contentCenterLongitudeDeg',
    'contentRangeMeters', 'contentHeadingDeg', 'contentTrackUp', 'contentAnchorV',
    'contentOrientation', 'frontBufferValid', 'frontTerrainGeneration',
    'frontViewGeneration', 'frontContentRevision', 'gpuContentRevision',
    'frontCenterLatitudeDeg', 'frontCenterLongitudeDeg', 'frontMapHeadingDeg',
    'operationHealthContentTicks', 'operationHealthContentCaptures',
    'operationHealthResolveCalls', 'operationHealthDirtyBatches',
    'operationHealthDirtySignalsCoalesced', 'operationHealthDirtyCommits',
    'operationHealthViewInvalidations', 'operationHealthMotionRefreshes',
    'operationHealthForcedProjectionRefreshes',
    'operationHealthProjectionExactRefreshes', 'operationHealthProjectionBridgeUses',
    'backRenderFrames', 'skippedBackRenderFrames', 'frontBufferSwaps']:
    ck(('RendererField("%s")' % field) in observer and field in renderer,
       'observer binding resolves existing renderer field: ' + field)

flight_instrument = (SRC / 'UI' / 'AERISFlightInstrument.cs').read_text()
for field in ['navigationDisplay', 'terrainTileRenderer', 'planMode',
              'planCenterLatitudeDeg', 'planCenterLongitudeDeg',
              'cachedFallbackMapHeading']:
    owner = nav if field != 'navigationDisplay' else flight_instrument
    ck(('GetField("%s"' % field) in observer and field in owner,
       'observer binding resolves existing ND field: ' + field)
ck('GetField("flightInstrument"' in observer and 'flightInstrument' in bootstrap,
   'observer binding resolves existing bootstrap flight instrument')

for forbidden, label in [
    ('.SetValue(', 'reflection field writes'),
    ('.Invoke(', 'reflection method invocation'),
    ('new Thread', 'private thread creation'),
    ('Task.Run', 'task-pool work creation'),
    ('FlightCtrlState', 'flight-control state access'),
    ('OnAutopilotUpdate', 'autopilot callback ownership'),
    ('FlightInputHandler', 'pilot input ownership')]:
    ck(forbidden not in observer, 'observer contains no ' + label)

ck('new AERISTerrainGpuTileRenderer' not in observer,
   'observer does not create a second terrain renderer')
ck('new AERISTerrainTileSystem' not in observer,
   'observer does not create a second terrain tile system')
ck('RequestedViewReady' in observer and 'RequestedViewReady' in renderer,
   'observer reads existing requested-view readiness without owning it')
ck('AERISLogger.Info("[OH_REV3_5_R011_TURN_CHURN]' in observer,
   'five-second telemetry uses existing bounded logger')
ck('R011' not in renderer,
   'R011 diagnostic marker is absent from R010 renderer implementation')

failed = [name for ok, name in checks if not ok]
print('\n[OH REV3.5 R011 TURNING VIEW CHURN OBSERVER] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + ', '.join(failed))
    raise SystemExit(1)
