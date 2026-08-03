#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2.5 altitude gate hotfix 1")
policy_path = SOURCE / "Terrain" / "AERISTerrainViewportActivationPolicy.cs"
awareness_path = SOURCE / "Terrain" / "AERISTerrainAwareness.cs"
tiles_path = SOURCE / "Terrain" / "AERISTerrainTileSystem.cs"
raster_path = SOURCE / "Terrain" / "AERISTerrainRasterWorker.cs"
gpu_raster_path = SOURCE / "Terrain" / "AERISTerrainGpuTileRasterizer.cs"
gpu_renderer_path = SOURCE / "Terrain" / "AERISTerrainGpuTileRenderer.cs"
nd_path = SOURCE / "UI" / "AERISNavigationDisplay.cs"
fdi_path = SOURCE / "UI" / "AERISFlightInstrument.cs"
csproj_path = SOURCE / "AERISFlightControl.csproj"
build_path = ROOT / "build_ubuntu.sh"
runner_path = ROOT / "Tools" / "run_v01800_cp25_acceptance.py"

for path, label in [
    (policy_path, "central activation policy source exists"),
    (awareness_path, "terrain awareness source exists"),
    (tiles_path, "terrain tile system source exists"),
    (raster_path, "terrain raster worker source exists"),
    (gpu_raster_path, "GPU rasterizer source exists"),
    (gpu_renderer_path, "GPU renderer source exists"),
    (nd_path, "navigation display source exists"),
    (fdi_path, "flight instrument source exists"),
    (csproj_path, "C# project source exists"),
    (build_path, "Ubuntu build entrypoint exists"),
    (runner_path, "CP2.5 acceptance runner exists"),
]:
    suite.check(path.is_file(), label)

policy = read(policy_path)
awareness = read(awareness_path)
tiles = read(tiles_path)
raster = read(raster_path)
gpu_raster = read(gpu_raster_path)
gpu_renderer = read(gpu_renderer_path)
nd = read(nd_path)
fdi = read(fdi_path)
csproj = read(csproj_path)
build = read(build_path)

suite.check("ReactivateBelowAltitudeAslMeters = 39500.0" in policy,
            "reactivation boundary is strictly below 39.5 km ASL")
suite.check("DeactivateAtOrAboveAltitudeAslMeters = 40500.0" in policy,
            "deactivation boundary is at or above 40.5 km ASL")
suite.check("currentAltitudeAslMeters >= DeactivateAtOrAboveAltitudeAslMeters" in policy,
            "upper altitude boundary uses inclusive OFF comparison")
suite.check("currentAltitudeAslMeters < ReactivateBelowAltitudeAslMeters" in policy,
            "lower altitude boundary uses strict ON comparison")
suite.check("39.5–40.5 KM HYSTERESIS HOLD" in policy,
            "middle altitude band explicitly preserves prior activation state")
suite.check("initialActive = currentAltitudeAslMeters <" in policy,
            "first evaluation in the hysteresis band uses deterministic initialization")

suite.check("readonly AERISTerrainViewportActivationPolicy viewportActivation;" in awareness,
            "terrain awareness owns the single central viewport policy")
suite.check("viewportActivation.Evaluate(flightEligible, altitudeAsl" in awareness,
            "terrain awareness evaluates ASL activation once per runtime tick")
suite.check("displayTiles.Tick(vessel, landing, airfields, flightViewportActive," in awareness and
            "landDetailActive);" in awareness,
            "central activation result is passed to the tile system")
suite.check(awareness.find("if (!flightViewportActive)") <
            awareness.find("if (!TerrainSamplingAvailable())"),
            "altitude OFF is enforced before any legacy PQS terrain sampling")
suite.check("[CP2.5/TERRAIN_ACTIVATION]" in awareness,
            "activation transitions are observable in the AERIS log")

suite.check("bool flightViewportEnabled" in tiles,
            "tile system has an explicit flight-viewport activation input")
suite.check(tiles.find("preloadBuilder.Tick(currentBody, flight)") <
            tiles.find("FLIGHT TERRAIN VIEWPORT ALTITUDE-GATED OFF"),
            "Preload Builder continues before the flight viewport altitude gate")
suite.check("FLIGHT TERRAIN VIEWPORT ALTITUDE-GATED OFF — PRELOAD CONTINUES" in tiles,
            "altitude OFF reports that Preload Builder remains active")
suite.check("desiredRequestIds.Clear();" in tiles and
            "desiredVisibleIds.Clear();" in tiles and
            "visibleKeys.Clear();" in tiles and
            "ReconcilePlannedRequests();" in tiles,
            "suspension removes desired viewport work and reconciles queued I/O")
suite.check("return flightViewportActive && HighLogic.LoadedSceneIsFlight" in tiles,
            "stale flight fallback work cannot commit while viewport is OFF")
suite.check("Status = \"FLIGHT TERRAIN VIEWPORT INACTIVE\"" in tiles,
            "CaptureVisible cannot publish stale terrain while viewport is OFF")

suite.check("internal void Suspend()" in raster and '"terrain-raster"' in raster,
            "legacy terrain raster job is cancelled on altitude transition")
suite.check("internal void CancelAll()" in gpu_raster and
            "pending.Clear();" in gpu_raster and "completed.Clear();" in gpu_raster,
            "GPU mesh preparation state is cancelled and cleared")
suite.check("internal void SuspendViewport()" in gpu_renderer and
            "rasterizer.CancelAll();" in gpu_renderer and
            "ReleaseGpuResources();" in gpu_renderer,
            "GPU meshes and RenderTexture resources are released on altitude OFF")

suite.check("internal void SetFlightViewportActive(bool active)" in nd,
            "navigation display has a transition lifecycle gate")
suite.check("terrainRasterWorker.Suspend();" in nd and
            "terrainTileRenderer.SuspendViewport();" in nd,
            "navigation display stops CPU and GPU terrain workers on transition")
suite.check("AERISPreparedNavigationFrameApi.Clear();" in nd and
            "AERISPreparedTrafficFrameApi.Clear();" in nd,
            "navigation and traffic snapshots are cleared")
suite.check("ClearInactiveFlightViewportState(activationChanged);" in awareness and
            "hasActiveGrid = false;" in awareness and
            "Array.Clear(activeFlags, 0, activeFlags.Length);" in awareness and
            "nextRebuildAllowedRealtime = 0f;" in awareness,
            "published PQS grid is discarded on OFF transition to prevent stale reactivation")
suite.check("if (settings == null || core == null || !flightViewportActive) return;" in nd,
            "ND draw has an independent hard guard")

suite.check("bool terrainViewportActive = core.Terrain != null &&" in fdi and
            "core.Terrain.FlightViewportActive;" in fdi,
            "flight instrument consumes the central activation state")
suite.check("bool ndDisplayViewportActive = terrainViewportActive && !ndOff;" in fdi and
            "bool ndVisible = ndDisplayViewportActive &&" in fdi,
            "ND panel and all internal runway/airport/LAND layers are hidden by altitude gate")
suite.check("bool fdiVisible = !fdiOff &&" in fdi and
            fdi.find("bool fdiVisible") < fdi.find("bool terrainViewportActive"),
            "FDI visibility remains independent from the Terrain altitude gate")

suite.check('<Compile Include="Terrain\\AERISTerrainViewportActivationPolicy.cs" />' in csproj,
            "central policy is included in xbuild project")
suite.check("run_v01800_cp25_acceptance.py" in build,
            "native build entrypoint runs CP2.5 acceptance")
suite.check("CP2.5 ALTITUDE GATE HOTFIX 1" in build,
            "native build identity exposes CP2.5 altitude gate")

all_track_a = "\n".join([policy, awareness, tiles, raster, gpu_raster,
                           gpu_renderer, nd, fdi])
suite.check("RunwayMasterCorrection" not in all_track_a and
            "CURATED_RUNWAY_GEODETIC_DEFAULTS" not in all_track_a,
            "Track A source contains no Track B curated runway integration")

suite.finish()
