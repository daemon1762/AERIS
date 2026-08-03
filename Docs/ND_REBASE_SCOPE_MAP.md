# CP3.75 ND Rebase Scope Map — Candidate 1

## Authorities

- Golden ND authority: `cp3-gate5-candidate14-golden` / commit `3a653b5`
- Golden archive SHA-256: `350758594c3bb8ab36eed5c096515dfd20e2989b96a2089c96c52a624093b140`
- Current/rejected AERIS20 baseline: `aeris20-cp3.5-gate4-candidate2-rejected`
- Current archive SHA-256: `aa473f1f77d0b3c5356290ecdfc1b0a07d120616eeba7307229bff8b6722656e`

## Exact Candidate14 rebase

These files changed after Candidate14 and belong to the ND terrain supply/presentation authority chain. Candidate 1 restores them byte-for-byte from Candidate14:

- `Source/AERISFlightControl/AERISFlightControl.csproj`
- `Source/AERISFlightControl/Performance/AERISMapDramCache.cs`
- `Source/AERISFlightControl/Performance/AERISNavigationDisplayPipeline.cs`
- `Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs`
- `Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs`
- `Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs`
- `Source/AERISFlightControl/Terrain/AERISPredictiveForwardCorridor.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainLandDetailActivationPolicy.cs` (restored)
- `Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs`
- `Source/AERISFlightControl/Terrain/AERISTerrainVirtualDetail.cs`
- `Source/AERISFlightControl/UI/AERISNavigationDisplay.cs`

This exact rebase removes the rejected CP3.5 Gate4 world-surface / virtual-upscale / projection-generation authority from the active runtime path.

## Narrow mixed-file merge

These files contain both ND-related and later independent non-ND work and therefore are **not** wholesale reverted:

### `Source/AERISFlightControl/Settings/AERISSettings.cs`
Candidate14 terrain-quality/LAND capability authority is restored. The later verified FDR/CVR archive-retention setting (`FlightDataArchiveLimit`, 1..30, default 10) is preserved.

### `GameData/AERISFlightControl/Config/AERISSettings.cfg`
Candidate14 terrain-quality revision/capability defaults are restored. `flightDataArchiveLimit = 10` is preserved.

### `Source/AERISFlightControl/Core/AERISBootstrap.cs`
Candidate14 terrain/ND startup description is restored. `AERISFlightDataArchive.ConfigureRetention(settings.FlightDataArchiveLimit)` is preserved.

### `Source/AERISFlightControl/UI/AERISWindow.cs`
The later non-ND responsive/compact UI, autopilot-category UI, runway-management UI and FDR/CVR archive UI are preserved. Only the terrain-quality selector semantics are restored to Candidate14 authority, including hidden LAND handling.

## Protected non-ND runtime

All runtime source files outside the explicit exact-rebase and mixed-merge list are immutable relative to AERIS20 Candidate2 for Candidate 1. This includes AA/FBW, Autopilot, PROTECT, FlightState, Integrations, FlightPlans, Landing control/observation, Logging, API and the complete `Recording/AERISFlightDataArchive.cs` implementation.

`GameData/AERISFlightControl/Airfields/` is also protected byte-for-byte to retain the 41 physical / 82 reciprocal runway authority and field-verified runway baselines.

## Build/verification-only changes

Candidate 1 may additionally change build identity, README/acceptance documentation, manifests and CP3.75 verification scripts. These are not runtime rollback scope.

## Candidate 1 exclusions

No rejected CP3.5 terrain presentation algorithm is intentionally ported. In particular Candidate 1 does not retain:

- Gate4 coarse world-surface upscale as cartographic authority
- REAL65/97 whole-map refinement authority
- CP3.5 projection-generation competition
- CP3.5 terrain-projection worker accounting
- CP3.5 removal of the Candidate14 LAND-detail supply contract

Performance optimization is deferred until the pure rebase passes runtime Gates C–F.
