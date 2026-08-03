#!/usr/bin/env python3
import re, sys
from pathlib import Path
sys.dont_write_bytecode=True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read

suite=CheckSuite('v0.18.0.0 CP3.5 Gate 3 Unified World Surface / Adaptive Hi-Res / Accessibility Candidate 1')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
virtual=read(SOURCE/'Terrain/AERISTerrainVirtualDetail.cs')
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
contracts=read(SOURCE/'Terrain/AERISTerrainTileContracts.cs')
perf=read(SOURCE/'Terrain/AERISTerrainPerformance.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
pipeline=read(SOURCE/'Performance/AERISNavigationDisplayPipeline.cs')
cache=read(SOURCE/'Terrain/AERISCurrentBodyResidentCache.cs')
preload=read(SOURCE/'Terrain/AERISTerrainPreloadBuilder.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')

identity='DEV CP3.5 GATE 3 — UNIFIED WORLD SURFACE / ADAPTIVE HI-RES / ACCESSIBILITY CANDIDATE 1'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,'generated source identity')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,'build generator identity')
suite.check('GATE 3 UNIFIED WORLD SURFACE ADAPTIVE HI RES ACCESSIBILITY CANDIDATE 1' in build,'build display identity')
suite.check('Gate 3 Unified World Surface / Adaptive Hi-Res / Accessibility Candidate 1' in avc,'AVC identity')
suite.check('run_v01800_cp35_gate3_candidate1_acceptance.py' in build,'build invokes Gate 3 acceptance')

# Terrain quality LAND is retired completely; LAND autopilot/display remains a separate domain.
retired=[
 'AERISTerrainQualityMode.Land','AERISTerrainTileLod.Land',
 'TerrainLandRuntimeQualityEnabled','AERISTerrainLandDetailActivationPolicy',
 'LandDetailActive','SetLandDetailActive','AERISResidentPinReason.Landing',
 'AERISTerrainRequestLane.Landing','LAND_SITES','terrainLandRuntimeQualityEnabled'
]
all_terrain='\n'.join(read(p) for p in list((SOURCE/'Terrain').rglob('*.cs'))+[SOURCE/'Settings/AERISSettings.cs',SOURCE/'UI/AERISWindow.cs',SOURCE/'Terrain/AERISCurrentBodyResidentCache.cs'])
for token in retired:
    suite.check(token not in all_terrain,'retired terrain-quality token absent: '+token)
suite.check(not (SOURCE/'Terrain/AERISTerrainLandDetailActivationPolicy.cs').exists(),'retired LAND detail policy source deleted')
suite.check('AERISTerrainLandDetailActivationPolicy.cs' not in read(SOURCE/'AERISFlightControl.csproj'),'project excludes retired LAND detail policy')
suite.check(re.search(r'internal enum AERISTerrainQualityMode\s*\{\s*Automatic\s*=\s*0,\s*Low\s*=\s*1,\s*Medium\s*=\s*2,\s*High\s*=\s*3\s*\}',settings,re.S) is not None,'quality enum is exactly AUTO/LOW/MEDIUM/HIGH')
suite.check('new string[]{"AUTO","LOW","MEDIUM","HIGH"}' in window,'quality selector is exactly four choices')
suite.check('CurrentTerrainQualityModelRevision = 3' in settings,'quality model migration revision advanced')
suite.check('retired hidden detail preset removed' in settings,'retired-quality migration is explicit')
suite.check('new AERISTerrainPerformanceProfile("LAND"' not in perf,'hidden LAND performance profile removed')
suite.check(perf.count('new AERISTerrainPerformanceProfile(')==3,'exactly three explicit performance profiles remain')
suite.check('Landing = ' not in contracts[contracts.index('internal enum AERISTerrainRequestLane'):contracts.index('internal enum AERISTerrainSamplingStage')],'request-lane LAND specialization removed')
suite.check('Land =' not in contracts[contracts.index('internal enum AERISTerrainTileLod'):contracts.index('internal enum AERISTerrainTilePriority')],'tile LOD LAND specialization removed')
suite.check('NavigationDisplayLandProfileSize' in settings and 'DrawLandingProfile' in nd,'LAND guidance/profile display remains independent and intact')

# Adaptive high-resolution terrain.
suite.check('viewportPixels' in virtual and 'targetCellPixels' in virtual,'virtual detail uses screen-space error')
suite.check('"ADAPTIVE ROUTE", 2, 65, 1.25f' in virtual,'adaptive route reconstruction is 65x65')
suite.check('"ADAPTIVE LOCAL", 4, 129, 1.50f' in virtual,'adaptive local reconstruction is 129x129')
suite.check('ResolveVirtualDetailProfile(rangeMeters, plot.height)' in renderer,'renderer supplies actual ND viewport pixels')
suite.check('AERISTerrainVirtualDetailPolicy.Resolve(quality, rangeMeters,' in renderer,'renderer calls screen-space policy')
suite.check('landScore >= 0.5f ? (byte)1 : (byte)2' in virtual,'coast classification receives sub-cell continuous reconstruction')
suite.check('does not extrapolate beyond a known' in virtual,'reconstruction bounded to authoritative FAR cells')
suite.check('AddAdaptiveExactDetailBridge(latitude, longitude, nearLod' in tiles,'progressive exact viewport refinement enabled')
suite.check('bool high = profile != null && string.Equals(profile.Name, "HIGH"' in tiles,'HIGH profile expands bounded exact refinement neighbourhood')
suite.check('int radius = high ? 1 : 0' in tiles,'exact refinement neighbourhood remains bounded')
suite.check('range <= 160000.0 ? AERISTerrainTileLod.Route' in tiles,'MEDIUM can request exact Route refinement at 160 km')
suite.check('AERISTerrainTilePriority.Critical' in tiles[tiles.index('void AddAdaptiveExactDetailBridge'):tiles.index('bool ExactDetailPayloadExists')],'existing exact detail wins refinement priority')

# Unified exact world surface.
suite.check('UNITY GPU UNIFIED WORLD SURFACE TEMPORAL REPROJECTION' in renderer,'GPU assist identifies unified world surface')
suite.check('Material worldSurfaceMaterial;' in renderer,'dedicated world-surface material exists')
suite.check('AERIS_ND_WORLD_SURFACE_MATERIAL' in renderer,'world-surface material has stable identity')
suite.check('SetWorldSurfaceNavigationFrame(AERISPreparedNavigationFrame frame' in renderer,'renderer accepts prepared navigation frame')
suite.check('IsWorldSurfaceNavigationCurrent(AERISPreparedNavigationFrame frame' in renderer,'renderer exposes exact surface-current latch')
suite.check('internal AERISPreparedNavigationFrame NavigationFrame;' in renderer,'projection batch carries immutable navigation frame')
suite.check('internal long WorldSurfaceRevision;' in renderer,'projection batch carries world-surface revision')
suite.check('NavigationFrame = worldSurfaceNavigationFrame' in renderer,'key frame snapshots current world navigation authority')
suite.check('DrawWorldSurfaceNavigation(batch, detailedProfile, ref profile);' in renderer,'navigation geometry is composed during authoritative BACK render')
suite.check('ProjectLatitudeLongitudeToGui(runway.LatitudeADeg' in renderer,'runways use shared exact geographic projection')
suite.check('ProjectLatitudeLongitudeToGui(facility.LatitudeDeg' in renderer,'facilities use shared exact geographic projection')
suite.check('GL.Begin(GL.QUADS)' in renderer and 'EmitWorldLineQuad' in renderer,'world geometry batches through one GPU quad pass')
suite.check('frontWorldSurfaceRevision = batch.WorldSurfaceRevision' in renderer,'FRONT latches matching world-surface revision')
suite.check('world_surface_avg_ms=' in renderer and 'world_surface_primitives_avg=' in renderer,'world-surface profiler fields emitted')
suite.check('LatitudeDeg;' in pipeline and 'LongitudeDeg;' in pipeline and 'HasGeographicPosition;' in pipeline,'prepared facilities preserve geography')
suite.check('LatitudeDeg = item.LatitudeDeg' in pipeline and 'LongitudeDeg = item.LongitudeDeg' in pipeline,'facility geography populated on worker pipeline')
suite.check('SetWorldSurfaceNavigationFrame(hasFrame ? frame : null' in nd,'ND publishes prepared navigation authority to GPU surface')
suite.check('IsWorldSurfaceNavigationCurrent(frame' in nd,'ND checks exact integrated FRONT before suppressing overlay geometry')
suite.check('!geometryAlreadyInWorldSurface' in nd,'IMGUI facility/runway geometry is suppressed only when integrated surface is current')
suite.check('previewOnlyHighlight' in nd,'dynamic runway preview highlight remains realtime')

# Accessibility palette generation and luminance-safe colors.
suite.check('HandlePaletteGeneration(currentPreset);' in renderer,'palette switch is generation-managed')
suite.check('paletteGeneration++' in renderer and 'gpuContentRevision++' in renderer,'palette change invalidates content generation')
suite.check('CancelProjectionBatch();' in renderer[renderer.index('void HandlePaletteGeneration'):renderer.index('void DrawWorldSurfaceNavigation')],'palette change cancels stale worker batch')
suite.check('ResetFrontBufferState();' in renderer[renderer.index('void HandlePaletteGeneration'):renderer.index('void DrawWorldSurfaceNavigation')],'palette change discards stale exact/presentation FRONT')
suite.check('[CP3.5/ACCESSIBILITY]' in renderer,'palette generation is telemetry-visible')
suite.check('new Color32(70, 84, 92, 255)' in renderer,'HIGH safe REL terrain is visible charcoal, not black')
suite.check('new Color32(255, 218, 52, 255)' in renderer,'HIGH caution retains high luminance contrast')
suite.check('new Color32(74, 215, 245, 255)' in renderer,'HIGH near-safe band remains distinct from sea')
suite.check('new Color32(72, 98, 78, 255)' in renderer,'BY safe band is separated from sea blue')
suite.check('new Color32(82, 96, 108, 255)' in renderer,'RG safe band is neutral/slate rather than sea blue')
suite.check('new Color32(220, 125, 215, 255)' in renderer,'BY caution avoids white saturation')
suite.check('new Color32(0, 20, 12, 255)' not in renderer,'old near-black HIGH safe color removed')
suite.check('new Color32(242, 235, 225, 255)' not in renderer,'old near-white BY caution color removed')
suite.check('return new Color32(8, 52, 118, 255);' in renderer,'water remains stable dark blue reference')
suite.check('0.24f : 0.48f' in renderer,'relief shading reduced to preserve accessibility bands')
suite.check('Mathf.Clamp(factor, 0.95f, 1.02f)' in renderer,'REL shading luminance range bounded')

# Gate 2 safety and UI lineage retained.
suite.check('static bool DrawPreparedEntry(' not in renderer,'Compile Hotfix 1 instance-material repair retained')
suite.check(re.search(r'\n\s*bool DrawPreparedEntry\(Entry entry, Matrix4x4 mapMatrix,',renderer) is not None,'DrawPreparedEntry remains instance method')
suite.check('GUI.matrix =' not in renderer and 'GUI.matrix=' not in renderer,'terrain temporal path never regresses to GUI.matrix warp')
suite.check('HistoryOverscanScale = 1.25f' in renderer,'overscan temporal surface retained')
suite.check('TemporalMaximumErrorPixels = 0.75f' in renderer,'sub-pixel temporal acceptance retained')
suite.check('string[] labels={"TAKEOFF","FLIGHT","NAV","LAND"}' in window,'AUTOPILOT child categories retained')
suite.check('wordWrap=false' in window or 'wordWrap = false' in window,'main UI no-wrap contract retained')
suite.check('ResponsiveWidth' in window and 'CompactControlHeight' in window,'responsive compact UI retained')

# Enum-reference static compile guard for retired members.
def enum_members(text,name):
    m=re.search(r'internal\s+enum\s+'+re.escape(name)+r'\s*\{([^}]*)\}',text,re.S)
    if not m: return set()
    return {x.split('=',1)[0].strip() for x in m.group(1).split(',') if x.split('=',1)[0].strip()}
all_cs='\n'.join(read(p) for p in SOURCE.rglob('*.cs'))
for enum_name, text in [('AERISTerrainTileLod',contracts),('AERISTerrainRequestLane',contracts),('AERISTerrainQualityMode',settings)]:
    members=enum_members(text,enum_name)
    refs=set(re.findall(re.escape(enum_name)+r'\.([A-Za-z_][A-Za-z0-9_]*)',all_cs))
    unknown=sorted(refs-members)
    suite.check(not unknown,enum_name+' references resolve to declared members',', '.join(unknown))

suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE3_UNIFIED_WORLD_SURFACE_ADAPTIVE_HIRES_ACCESSIBILITY_CANDIDATE1.txt').is_file(),'Gate 3 acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE3_UNIFIED_WORLD_SURFACE_ADAPTIVE_HIRES_ACCESSIBILITY_CANDIDATE1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Gate 3 runtime test card included')
suite.check((ROOT/'Docs/CP3.5_ND_PRESENTATION_PERFORMANCE_ROADMAP_REVISED_GATE3_2026-08-03_ja.md').is_file(),'revised CP3.5 roadmap included')
suite.finish()
