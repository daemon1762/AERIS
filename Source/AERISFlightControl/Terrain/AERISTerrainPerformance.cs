using System;
using UnityEngine;
using AERISFlightControl.Settings;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    internal sealed class AERISTerrainPerformanceProfile
    {
        internal readonly string Name;
        internal readonly int GridColumns;
        internal readonly int GridRows;
        internal readonly int TextureScale;
        internal readonly float PqsQueriesPerSecond;
        internal readonly int MaximumSamplesPerFrame;
        internal readonly float MinimumGridRefreshSeconds;
        internal readonly int MaximumFacilitySymbols;
        internal readonly float MaximumTerrainFps;
        internal readonly float MaximumNavigationFps;
        internal readonly float MaximumSymbologyFps;
        internal readonly int MaximumTerrainTileRequests;
        internal readonly int LocalTileRadius;
        internal readonly int MaximumConcurrentTileIo;
        internal readonly float TilePqsQueriesPerSecond;
        internal readonly int MaximumTileSamplesPerFrame;
        internal readonly float TileMainThreadBudgetMs;
        internal readonly AERISTerrainTileLod MaximumNormalLod;
        internal readonly int DefaultRamCacheMiB;
        internal readonly int DefaultDiskCacheMiB;
        internal readonly int DefaultVramCacheMiB;

        internal AERISTerrainPerformanceProfile(string name, int columns, int rows,
            int textureScale, float pqsQueriesPerSecond, int maximumSamplesPerFrame,
            float minimumGridRefreshSeconds, int maximumFacilitySymbols,
            float maximumTerrainFps, float maximumNavigationFps, float maximumSymbologyFps,
            int maximumTerrainTileRequests, int localTileRadius, int maximumConcurrentTileIo,
            float tilePqsQueriesPerSecond, int maximumTileSamplesPerFrame,
            float tileMainThreadBudgetMs, AERISTerrainTileLod maximumNormalLod,
            int defaultRamCacheMiB,
            int defaultDiskCacheMiB, int defaultVramCacheMiB)
        {
            Name = name;
            GridColumns = columns;
            GridRows = rows;
            TextureScale = textureScale;
            PqsQueriesPerSecond = pqsQueriesPerSecond;
            MaximumSamplesPerFrame = maximumSamplesPerFrame;
            MinimumGridRefreshSeconds = minimumGridRefreshSeconds;
            MaximumFacilitySymbols = maximumFacilitySymbols;
            MaximumTerrainFps = maximumTerrainFps;
            MaximumNavigationFps = maximumNavigationFps;
            MaximumSymbologyFps = maximumSymbologyFps;
            MaximumTerrainTileRequests = maximumTerrainTileRequests;
            LocalTileRadius = localTileRadius;
            MaximumConcurrentTileIo = maximumConcurrentTileIo;
            TilePqsQueriesPerSecond = tilePqsQueriesPerSecond;
            MaximumTileSamplesPerFrame = maximumTileSamplesPerFrame;
            TileMainThreadBudgetMs = tileMainThreadBudgetMs;
            MaximumNormalLod = maximumNormalLod;
            DefaultRamCacheMiB = defaultRamCacheMiB;
            DefaultDiskCacheMiB = defaultDiskCacheMiB;
            DefaultVramCacheMiB = defaultVramCacheMiB;
        }
    }

    // Adaptive, observation-only ND scheduler. It never changes KSP quality settings and
    // never writes any flight-control state. AUTO reacts to measured frame time and to
    // AERIS' own main-thread/worker cost, not to CPU/GPU model names.
    internal sealed class AERISTerrainPerformanceController
    {
        // Frozen CP2 profile audit aliases (documentation only):
        // "ECO", 13, 9 | "BALANCED", 17, 11 | "HIGH", 21, 15 | "ULTRA", 25, 17
        static readonly AERISTerrainPerformanceProfile[] Profiles =
        {
            new AERISTerrainPerformanceProfile("LOW", 13, 9, 3, 25f, 1, 2.0f, 12, 1.5f, 15f, 30f,
                18, 0, 1, 120f, 6, 0.35f, AERISTerrainTileLod.Route, 64, 512, 48),
            new AERISTerrainPerformanceProfile("MEDIUM", 17, 11, 3, 60f, 2, 1.0f, 18, 3f, 24f, 45f,
                32, 1, 2, 360f, 16, 0.75f, AERISTerrainTileLod.Local, 128, 1024, 96),
            new AERISTerrainPerformanceProfile("HIGH", 21, 15, 4, 120f, 4, 0.55f, 24, 5f, 30f, 60f,
                48, 1, 3, 720f, 32, 1.25f, AERISTerrainTileLod.Local, 256, 2048, 192),
            new AERISTerrainPerformanceProfile("LAND", 25, 17, 4, 240f, 6, 0.30f, 32, 8f, 45f, 60f,
                64, 2, 4, 1200f, 48, 1.80f, AERISTerrainTileLod.Land, 512, 4096, 384),
        };
        const int MaximumAutomaticQualityIndex = 2;

        readonly AERISSettings settings;
        int automaticQualityIndex = 1;
        int automaticRateTier = 2;
        float frameTimeEmaMs = 16.7f;
        float ndMainThreadEmaMs;
        float ndLayoutEmaMs;
        float ndRepaintEmaMs;
        float pqsSampleEmaMs;
        float tilePqsSampleEmaMs;
        float workerEmaMs;
        float terrainMeshWorkerEmaMs;
        float terrainContourWorkerEmaMs;
        float overloadSeconds;
        float recoverySeconds;
        float nextEvaluationRealtime;
        float lastFrameRealtime;
        bool workerBacklogged;
        bool landDetailActive;
        bool externalMaintenanceActive;
        bool maintenanceFreezeLogged;
        float maintenanceHoldUntilRealtime;
        int profileRevision;

        internal AERISTerrainPerformanceController(AERISSettings settings)
        {
            this.settings = settings;
        }

        internal int ProfileRevision { get { return profileRevision; } }
        internal float FrameTimeEmaMs { get { return frameTimeEmaMs; } }
        internal float NdMainThreadEmaMs { get { return ndMainThreadEmaMs; } }
        internal float NdLayoutEmaMs { get { return ndLayoutEmaMs; } }
        internal float NdRepaintEmaMs { get { return ndRepaintEmaMs; } }
        internal float PqsSampleEmaMs { get { return pqsSampleEmaMs; } }
        internal float TilePqsSampleEmaMs { get { return tilePqsSampleEmaMs; } }
        internal float WorkerEmaMs { get { return workerEmaMs; } }
        internal float TerrainMeshWorkerEmaMs { get { return terrainMeshWorkerEmaMs; } }
        internal float TerrainContourWorkerEmaMs { get { return terrainContourWorkerEmaMs; } }
        internal bool WorkerBacklogged { get { return workerBacklogged; } }

        internal AERISTerrainPerformanceProfile ActiveProfile
        {
            get
            {
                // CP3.75 Candidate 2: Golden LOW is the single runtime quality authority.
                // Legacy MEDIUM/HIGH/LAND profiles remain defined only for source/history
                // compatibility and are intentionally unreachable at runtime.
                return Profiles[0];
            }
        }

        internal string EffectiveQualityName { get { return ActiveProfile.Name; } }
        internal bool LandDetailActive { get { return landDetailActive; } }

        internal void SetLandDetailActive(bool active)
        {
            if (landDetailActive == active) return;
            landDetailActive = active;
            profileRevision++;
        }
        internal void SetExternalMaintenanceActive(bool active)
        {
            if (active)
            {
                externalMaintenanceActive = true;
                maintenanceHoldUntilRealtime = Time.realtimeSinceStartup + 5f;
                if (!maintenanceFreezeLogged)
                {
                    maintenanceFreezeLogged = true;
                    AERISLogger.Info("[ND/TERRAIN] AUTO quality/rate hold: " +
                        "airfield maintenance isolated from terrain adaptation");
                }
                return;
            }
            externalMaintenanceActive = false;
        }

        internal bool ExternalMaintenanceHold
        {
            get
            {
                return externalMaintenanceActive ||
                    Time.realtimeSinceStartup < maintenanceHoldUntilRealtime;
            }
        }
        internal bool QualityIsAutomatic
        {
            get { return false; }
        }
        internal bool UpdateRateIsAutomatic
        {
            get { return false; }
        }

        internal float EffectiveTerrainFps
        {
            get
            {
                AERISTerrainPerformanceProfile profile = ActiveProfile;
                float requested = ResolveRequestedUpdateFps();
                float layer = Mathf.Max(1f, requested / 6f);
                return Mathf.Clamp(Mathf.Min(layer, profile.MaximumTerrainFps), 0.5f, 12f);
            }
        }

        internal float EffectiveNavigationFps
        {
            get
            {
                AERISTerrainPerformanceProfile profile = ActiveProfile;
                float requested = ResolveRequestedUpdateFps();
                return Mathf.Clamp(Mathf.Min(requested, profile.MaximumNavigationFps), 5f, 60f);
            }
        }


        internal float EffectiveTilePlanningFps
        {
            get
            {
                // Keep tile planning independent from the user-facing ND cadence.
                // The former AUTO tier state is retired in Candidate 2.
                return 2f;
            }
        }

        internal float EffectiveSymbologyFps
        {
            get
            {
                AERISTerrainPerformanceProfile profile = ActiveProfile;
                float requested = ResolveRequestedUpdateFps();
                return Mathf.Clamp(Mathf.Min(requested, profile.MaximumSymbologyFps), 10f, 60f);
            }
        }

        internal void TickFrame()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Time.unscaledDeltaTime;
            if (!Finite(dt) || dt <= 0f || dt > 0.5f)
            {
                if (lastFrameRealtime > 0f) dt = Mathf.Clamp(now - lastFrameRealtime, 0.001f, 0.5f);
                else dt = 1f / 60f;
            }
            lastFrameRealtime = now;
            frameTimeEmaMs = Smooth(frameTimeEmaMs, dt * 1000f, 0.055f);
            if (now < nextEvaluationRealtime) return;
            nextEvaluationRealtime = now + 1f;
            EvaluateAdaptiveState();
        }

        internal void RecordNdMainThreadCost(float milliseconds)
        {
            RecordNdRepaintCost(milliseconds);
        }

        internal void RecordNdLayoutCost(float milliseconds)
        {
            if (!Finite(milliseconds) || milliseconds < 0f || milliseconds > 1000f) return;
            ndLayoutEmaMs = Smooth(ndLayoutEmaMs, milliseconds, 0.12f);
            UpdateNdAggregate();
        }

        internal void RecordNdRepaintCost(float milliseconds)
        {
            if (!Finite(milliseconds) || milliseconds < 0f || milliseconds > 1000f) return;
            ndRepaintEmaMs = Smooth(ndRepaintEmaMs, milliseconds, 0.12f);
            UpdateNdAggregate();
        }

        void UpdateNdAggregate()
        {
            ndMainThreadEmaMs = Mathf.Max(0f, ndLayoutEmaMs) +
                Mathf.Max(0f, ndRepaintEmaMs);
        }

        internal void RecordPqsSampleCost(float milliseconds)
        {
            if (!Finite(milliseconds) || milliseconds < 0f || milliseconds > 1000f) return;
            pqsSampleEmaMs = Smooth(pqsSampleEmaMs, milliseconds, 0.10f);
        }

        internal void RecordTilePqsCost(float milliseconds)
        {
            if (!Finite(milliseconds) || milliseconds < 0f || milliseconds > 1000f) return;
            tilePqsSampleEmaMs = Smooth(tilePqsSampleEmaMs, milliseconds, 0.08f);
        }

        internal void RecordWorkerCost(float milliseconds, bool backlog)
        {
            if (Finite(milliseconds) && milliseconds >= 0f && milliseconds <= 5000f)
                workerEmaMs = Smooth(workerEmaMs, milliseconds, 0.10f);
            // Several terrain workers may report during one one-second evaluation
            // window. A later healthy report must not erase an earlier backlog signal.
            workerBacklogged = workerBacklogged || backlog;
        }

        internal void RecordGpuMeshPreparation(float meshMilliseconds,
            float contourMilliseconds, bool backlog)
        {
            float mesh = Finite(meshMilliseconds) && meshMilliseconds >= 0f &&
                meshMilliseconds <= 5000f ? meshMilliseconds : 0f;
            float contour = Finite(contourMilliseconds) && contourMilliseconds >= 0f &&
                contourMilliseconds <= 5000f ? contourMilliseconds : 0f;
            if (mesh > 0f) terrainMeshWorkerEmaMs =
                Smooth(terrainMeshWorkerEmaMs, mesh, 0.10f);
            if (contour > 0f) terrainContourWorkerEmaMs =
                Smooth(terrainContourWorkerEmaMs, contour, 0.10f);
            RecordWorkerCost(mesh + contour, backlog);
        }

        internal void ClearWorkerBacklogFlag()
        {
            workerBacklogged = false;
        }

        void EvaluateAdaptiveState()
        {
            // CP3.75 Candidate 2 retires user/AUTO quality and update-rate adaptation.
            // Telemetry collection remains active, but runtime presentation authority is
            // fixed to Golden LOW + 10 Hz so measured load cannot silently change quality.
            if (!QualityIsAutomatic && !UpdateRateIsAutomatic)
            {
                overloadSeconds = 0f;
                recoverySeconds = 0f;
                workerBacklogged = false;
                return;
            }
            if (ExternalMaintenanceHold)
            {
                overloadSeconds = 0f;
                recoverySeconds = 0f;
                workerBacklogged = false;
                return;
            }
            if (maintenanceFreezeLogged)
            {
                maintenanceFreezeLogged = false;
                AERISLogger.Info("[ND/TERRAIN] AUTO adaptation resumed after airfield maintenance");
            }
            float ownMainCost = ndMainThreadEmaMs + pqsSampleEmaMs + tilePqsSampleEmaMs;
            bool severe = ndMainThreadEmaMs > 3.0f || pqsSampleEmaMs > 2.0f ||
                tilePqsSampleEmaMs > 1.25f ||
                (frameTimeEmaMs > 45f && ownMainCost > 0.6f) || workerBacklogged;
            bool overloaded = severe || ndMainThreadEmaMs > 1.6f || pqsSampleEmaMs > 1.0f ||
                tilePqsSampleEmaMs > 0.65f ||
                (frameTimeEmaMs > 33f && ownMainCost > 0.45f);
            bool comfortable = frameTimeEmaMs < 23f && ndMainThreadEmaMs < 0.75f &&
                pqsSampleEmaMs < 0.45f && tilePqsSampleEmaMs < 0.30f &&
                workerEmaMs < 8f && !workerBacklogged;

            if (overloaded)
            {
                overloadSeconds += 1f;
                recoverySeconds = 0f;
            }
            else if (comfortable)
            {
                recoverySeconds += 1f;
                overloadSeconds = Mathf.Max(0f, overloadSeconds - 1f);
            }
            else
            {
                overloadSeconds = Mathf.Max(0f, overloadSeconds - 0.5f);
                recoverySeconds = Mathf.Max(0f, recoverySeconds - 0.5f);
            }

            bool rateNeedsDownshift = UpdateRateIsAutomatic &&
                (severe || overloadSeconds >= 2f) && automaticRateTier > 0;
            // Quality is reduced immediately only when AERIS' own main-thread work is
            // clearly expensive. A worker backlog or a general KSP frame spike first
            // reduces refresh rate, avoiding an unnecessary PQS grid rebuild.
            bool qualitySevere = ndMainThreadEmaMs > 3.0f || pqsSampleEmaMs > 2.0f ||
                tilePqsSampleEmaMs > 1.25f ||
                (workerBacklogged && automaticRateTier == 0);
            bool qualityNeedsDownshift = QualityIsAutomatic &&
                (qualitySevere || overloadSeconds >= 5f) && automaticQualityIndex > 0;

            if (rateNeedsDownshift)
            {
                automaticRateTier--;
                overloadSeconds = 0f;
                recoverySeconds = 0f;
                AERISLogger.Info("[ND/TERRAIN] AUTO rate tier=" +
                    automaticRateTier + " (load protection)");
            }

            if (qualityNeedsDownshift)
            {
                automaticQualityIndex--;
                overloadSeconds = 0f;
                recoverySeconds = 0f;
                profileRevision++;
                AERISLogger.Info("[ND/TERRAIN] AUTO quality=" +
                    Profiles[automaticQualityIndex].Name + " (load protection)");
            }

            if (UpdateRateIsAutomatic && recoverySeconds >= 25f && automaticRateTier < 4)
            {
                automaticRateTier++;
                recoverySeconds = 0f;
                AERISLogger.Info("[ND/TERRAIN] AUTO rate tier=" +
                    automaticRateTier + " (recovery)");
            }
            else if (QualityIsAutomatic && recoverySeconds >= 55f &&
                automaticQualityIndex < MaximumAutomaticQualityIndex)
            {
                automaticQualityIndex++;
                recoverySeconds = 0f;
                profileRevision++;
                AERISLogger.Info("[ND/TERRAIN] AUTO quality=" +
                    Profiles[automaticQualityIndex].Name + " (recovery)");
            }

            workerBacklogged = false;
        }

        int QualityIndexFromSettings()
        {
            return 0;
        }

        float ResolveRequestedUpdateFps()
        {
            return AERISSettings.FixedNavigationDisplayUpdateHz;
        }

        float AutoTerrainFps()
        {
            switch (automaticRateTier)
            {
                case 0: return 1f;
                case 1: return 2f;
                case 2: return 3f;
                case 3: return 5f;
                default: return 10f;
            }
        }

        float AutoNavigationFps()
        {
            switch (automaticRateTier)
            {
                case 0: return 10f;
                case 1: return 15f;
                case 2: return 24f;
                case 3: return 30f;
                default: return 45f;
            }
        }

        float AutoSymbologyFps()
        {
            switch (automaticRateTier)
            {
                case 0: return 20f;
                case 1: return 30f;
                case 2: return 45f;
                default: return 60f;
            }
        }

        static float Smooth(float current, float value, float alpha)
        {
            if (!Finite(current) || current <= 0f) return value;
            return current + (value - current) * Mathf.Clamp01(alpha);
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
