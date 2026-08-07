#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
ND=(ROOT/'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs').read_text()
RENDERER=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
RF=''.join(RENDERER.split())
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('long terrainSymbologyFrontSwap = -1L' in ND,'terrain-synchronized symbology owns a FRONT swap generation')
ck('long frontSwap = terrainTileRenderer.FrontBufferSwaps' in ND,'actual Terrain FRONT swap is the symbology update clock')
ck('terrainSymbologyFrontSwap == frontSwap' in ND,'symbology snapshot is held between Terrain FRONT swaps')
ck('terrainSymbologyLatitudeDeg = presented.CenterLatitudeDeg' in ND and 'terrainSymbologyLongitudeDeg = presented.CenterLongitudeDeg' in ND,'ownship display position is committed from presented terrain projection')
ck('displayOwnLatitudeDeg = terrainSymbologyValid ?' in ND and 'displayOwnLongitudeDeg = terrainSymbologyValid ?' in ND,'aircraft map point consumes synchronized ownship snapshot')
draw_start=ND.index('void DrawLocal('); draw_end=ND.index('void UpdateAuxiliarySnapshots',draw_start); draw=ND[draw_start:draw_end]
map_anchor=draw.index('double ownEast = 0.0, ownNorth = 0.0;'); map_tail=draw.index('Vector2 aircraftPoint;',map_anchor); own_block=draw[map_anchor:map_tail]
ck('displayOwnLatitudeDeg' in own_block and 'displayOwnLongitudeDeg' in own_block and 'frame.OriginLongitudeDeg, vessel.latitude, vessel.longitude' not in own_block,'aircraft position no longer mixes live vessel with committed terrain FRONT')
ck('DrawRangeRings(plan, aircraftPoint, scale)' in draw,'range fan remains rigidly centered on synchronized aircraft point')
track_start=ND.index('void DrawTrackVector('); track_end=ND.index('void DrawPreparedTraffic(',track_start); track=ND[track_start:track_end]
ck('terrainSymbologyLatitudeDeg : vessel.latitude' in track and 'terrainSymbologyLongitudeDeg : vessel.longitude' in track,'track vector origin uses synchronized ownship position')
ck('terrainSymbologyGroundSpeedMps : vessel.srfSpeed' in track,'track vector speed uses Terrain FRONT snapshot')
ck('terrainSymbologyGroundTrackDeg : cachedFallbackMapHeading' in track,'track vector direction uses Terrain FRONT snapshot')
ck('terrainSymbologyGroundSpeedMps = Math.Max(0.0, vessel.srfSpeed)' in ND and 'terrainSymbologyGroundTrackDeg = ResolveMapHeading(vessel)' in ND,'speed and track are captured atomically on FRONT swap')
ck('terrainPresentationActive && !planMode' in draw,'PLAN mode remains independent from moving-map FRONT synchronization')
ck('FrontBufferSwaps' in RENDERER,'renderer publishes Terrain FRONT swap authority')
ck('ProjectionRefreshAgeSeconds=0.50f' in RF and (('nextBackRefreshRealtime=Time.realtimeSinceStartup+0.10f' in RF) or ('nextBackRefreshRealtime=nextAuthoritativePresentationTickRealtime' in RF and 'nextAuthoritativePresentationTickRealtime=presentationNow+0.10f' in RF)),'terrain presentation cadence policy remains fixed at 10 Hz under approved shared authority')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Pass 1 Hotfix 1 symbology sync] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed: print('FAILED: '+', '.join(failed)); raise SystemExit(1)
