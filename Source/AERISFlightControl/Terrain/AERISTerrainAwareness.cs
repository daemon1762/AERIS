using System;
using System.Globalization;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using AERISFlightControl.Landing;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    internal enum AERISTerrainAlertLevel
    {
        None = 0,
        Terrain = 1,
        TerrainAhead = 2,
        PullUp = 3
    }

    // Display-only terrain observer. It samples CelestialBody terrain incrementally
    // on the Unity/KSP main thread and never writes FlightCtrlState or any AP demand.
    internal sealed class AERISTerrainAwareness
    {
        internal const int MaximumGridColumns = 25;
        internal const int MaximumGridRows = 17;
        const int MaximumGridCells = MaximumGridColumns * MaximumGridRows;

        readonly AERISSettings settings;
        readonly AERISTerrainPerformanceController performance;
        readonly AERISTerrainTileSystem displayTiles;
        readonly AERISTerrainViewportActivationPolicy viewportActivation;
        readonly AERISTerrainLandDetailActivationPolicy landDetailActivation;
        readonly float[] activeElevation = new float[MaximumGridCells];
        readonly byte[] activeFlags = new byte[MaximumGridCells];
        readonly float[] pendingElevation = new float[MaximumGridCells];
        readonly byte[] pendingFlags = new byte[MaximumGridCells];

        delegate double TerrainAltitude2(CelestialBody body, double latitude, double longitude);
        delegate double TerrainAltitude3(CelestialBody body, double latitude, double longitude,
            bool allowNegative);

        static bool terrainMethodResolved;
        static MethodInfo terrainAltitudeMethod;
        static TerrainAltitude2 terrainAltitude2;
        static TerrainAltitude3 terrainAltitude3;
        static readonly object[] reflectionArgs2 = new object[2];
        static readonly object[] reflectionArgs3 = new object[3];
        int pendingIndex;
        int pendingValidCount;
        int activeColumns = 13;
        int activeRows = 9;
        int pendingColumns = 13;
        int pendingRows = 9;
        int activeProfileRevision;
        int pendingProfileRevision;
        float sampleBudget;
        float lastSamplerRealtime;
        bool rebuilding;
        bool hasActiveGrid;
        string activeBody = string.Empty;
        string pendingBody = string.Empty;
        double activeLatitude;
        double activeLongitude;
        double pendingLatitude;
        double pendingLongitude;
        float activeRangeMeters;
        float pendingRangeMeters;
        float activeHeadingDeg;
        float pendingHeadingDeg;
        bool activeTrackUp;
        bool pendingTrackUp;
        float nextRebuildAllowedRealtime;
        float nextFaultLogRealtime;
        static float nextSharedFaultLogRealtime;
        int gridRevision;
        float alertHoldUntil;
        AERISTerrainAlertLevel heldAlert;

        internal AERISTerrainAwareness(AERISSettings settings)
            : this(settings, null)
        {
        }

        internal AERISTerrainAwareness(AERISSettings settings,
            AERISMapDramCache mapDramCache)
        {
            this.settings = settings;
            performance = new AERISTerrainPerformanceController(settings);
            viewportActivation = new AERISTerrainViewportActivationPolicy();
            landDetailActivation = new AERISTerrainLandDetailActivationPolicy();
            displayTiles = new AERISTerrainTileSystem(settings, performance,
                mapDramCache);
        }

        internal AERISTerrainPerformanceController Performance { get { return performance; } }
        internal AERISTerrainTileSystem DisplayTiles { get { return displayTiles; } }
        internal AERISCurrentBodyResidentCache CurrentBodyResidentCache
        {
            get { return displayTiles == null ? null :
                displayTiles.CurrentBodyResidentCache; }
        }
        internal AERISTerrainViewportActivationPolicy ViewportActivation
        {
            get { return viewportActivation; }
        }
        internal AERISTerrainLandDetailActivationPolicy LandDetailActivation
        {
            get { return landDetailActivation; }
        }
        internal bool FlightViewportActive
        {
            get { return viewportActivation != null && viewportActivation.Active; }
        }
        internal int GridColumns { get { return hasActiveGrid ? activeColumns : pendingColumns; } }
        internal int GridRows { get { return hasActiveGrid ? activeRows : pendingRows; } }
        internal int ActiveProfileRevision { get { return activeProfileRevision; } }
        internal bool DataAvailable { get { return TerrainSamplingAvailable(); } }
        internal bool GridReady { get { return hasActiveGrid; } }
        internal bool GridUpdating { get { return rebuilding; } }
        internal int GridRevision { get { return gridRevision; } }
        internal string StatusText { get; private set; } = "TERRAIN DATA INITIALIZING";
        internal float RangeMeters { get { return hasActiveGrid ? activeRangeMeters : pendingRangeMeters; } }
        internal float MapHeadingDeg { get { return hasActiveGrid ? activeHeadingDeg : pendingHeadingDeg; } }
        internal bool TrackUp { get { return hasActiveGrid ? activeTrackUp : pendingTrackUp; } }
        internal float AircraftAltitudeAslMeters { get; private set; }
        internal float RadarAltitudeMeters { get; private set; }
        internal float GroundSpeedMetersPerSecond { get; private set; }
        internal float VerticalSpeedMetersPerSecond { get; private set; }
        internal float MinimumPredictedClearanceMeters { get; private set; } = float.PositiveInfinity;
        internal float MinimumPredictedClearanceSeconds { get; private set; } = float.PositiveInfinity;
        internal AERISTerrainAlertLevel AlertLevel { get; private set; }
        internal string AlertText
        {
            get
            {
                return AlertLevel == AERISTerrainAlertLevel.PullUp ? "PULL UP" :
                    (AlertLevel == AERISTerrainAlertLevel.TerrainAhead ? "TERRAIN AHEAD" :
                    (AlertLevel == AERISTerrainAlertLevel.Terrain ? "TERRAIN" : string.Empty));
            }
        }

        internal void Reset(string reason)
        {
            rebuilding = false;
            pendingIndex = 0;
            pendingValidCount = 0;
            sampleBudget = 0f;
            lastSamplerRealtime = 0f;
            hasActiveGrid = false;
            gridRevision++;
            activeBody = pendingBody = string.Empty;
            AlertLevel = AERISTerrainAlertLevel.None;
            heldAlert = AERISTerrainAlertLevel.None;
            alertHoldUntil = 0f;
            MinimumPredictedClearanceMeters = float.PositiveInfinity;
            MinimumPredictedClearanceSeconds = float.PositiveInfinity;
            StatusText = string.IsNullOrEmpty(reason) ? "TERRAIN DATA RESET" :
                "TERRAIN DATA RESET: " + reason.ToUpperInvariant();
            Array.Clear(activeFlags, 0, activeFlags.Length);
            Array.Clear(pendingFlags, 0, pendingFlags.Length);
            if (viewportActivation != null) viewportActivation.Reset(reason);
            if (landDetailActivation != null) landDetailActivation.Reset(reason);
            if (performance != null) performance.SetLandDetailActive(false);
            if (displayTiles != null) displayTiles.Reset(reason);
        }

        internal void Tick(Vessel vessel, AERISLandingFoundation landing)
        {
            Tick(vessel, landing, null);
        }

        internal void Tick(Vessel vessel, AERISLandingFoundation landing,
            AERISAirfieldRegistry airfields)
        {
            if (performance != null)
            {
                bool airfieldMaintenanceActive = airfields != null &&
                    (airfields.ReloadState == AERISAirfieldReloadState.LoadingCache ||
                     airfields.ReloadState == AERISAirfieldReloadState.Discovering ||
                     airfields.ReloadState == AERISAirfieldReloadState.Surveying ||
                     airfields.ReloadState == AERISAirfieldReloadState.Validating ||
                     airfields.ReloadState == AERISAirfieldReloadState.Staged);
                performance.SetExternalMaintenanceActive(airfieldMaintenanceActive);
                performance.TickFrame();
            }
            bool flightEligible = HighLogic.LoadedSceneIsFlight && vessel != null &&
                vessel.mainBody != null;
            double altitudeAsl = flightEligible && Finite(vessel.altitude) ?
                vessel.altitude : double.NaN;
            bool activationChanged = viewportActivation != null &&
                viewportActivation.Evaluate(flightEligible, altitudeAsl,
                    vessel == null || vessel.mainBody == null ? string.Empty :
                    vessel.mainBody.name);
            bool flightViewportActive = viewportActivation != null &&
                viewportActivation.Active;
            if (viewportActivation != null && activationChanged)
                AERISLogger.Info("[CP2.5/TERRAIN_ACTIVATION] " +
                    viewportActivation.StatusText + " | body=" +
                    viewportActivation.BodyName + " | altitude_asl_m=" +
                    (Finite(viewportActivation.AltitudeAslMeters) ?
                        viewportActivation.AltitudeAslMeters.ToString("0.0",
                            CultureInfo.InvariantCulture) : "N/A"));

            bool landArmDemand = landing != null && landing.Armed;
            // Approach and Auto Landing are explicit future demand inputs. They remain
            // false while independent LAND is observation-only and legacy NAV is absent.
            bool landActivationChanged = landDetailActivation != null &&
                landDetailActivation.Evaluate(flightEligible, flightViewportActive,
                    settings != null && settings.TerrainLandRuntimeQualityEnabled,
                    landArmDemand, false, false,
                    vessel == null || vessel.mainBody == null ? string.Empty :
                    vessel.mainBody.name);
            bool landDetailActive = landDetailActivation != null &&
                landDetailActivation.Active;
            if (performance != null) performance.SetLandDetailActive(landDetailActive);
            if (landActivationChanged)
                AERISLogger.Info("[CP2.5/LAND_DETAIL] " +
                    landDetailActivation.StatusText + " | body=" +
                    landDetailActivation.BodyName + " | enabled=" +
                    landDetailActivation.CapabilityEnabled + " | demand=" +
                    AERISTerrainLandDetailActivationPolicy.FormatDemand(
                        landDetailActivation.Demand) + " | profile=" +
                    (performance == null ? "N/A" :
                        performance.EffectiveQualityName));

            // Preload generation must continue in safe non-Flight scenes and while the
            // flight viewport is altitude-gated OFF. Only viewport request generation,
            // fallback sampling and display-visible terrain work are suspended. LAND
            // runtime requests additionally require the central Gate 3 demand.
            if (displayTiles != null)
                displayTiles.Tick(vessel, landing, airfields, flightViewportActive,
                    landDetailActive);
            if (!flightEligible)
            {
                ClearInactiveFlightViewportState(activationChanged);
                return;
            }

            AircraftAltitudeAslMeters = Finite(vessel.altitude) ? (float)vessel.altitude : 0f;
            RadarAltitudeMeters = Finite(vessel.heightFromTerrain) && vessel.heightFromTerrain >= 0.0 ?
                Mathf.Max(0f, (float)vessel.heightFromTerrain) : float.PositiveInfinity;
            GroundSpeedMetersPerSecond = Finite(vessel.srfSpeed) ? Mathf.Max(0f, (float)vessel.srfSpeed) : 0f;
            VerticalSpeedMetersPerSecond = Finite(vessel.verticalSpeed) ? (float)vessel.verticalSpeed : 0f;

            if (!flightViewportActive)
            {
                ClearInactiveFlightViewportState(activationChanged);
                StatusText = viewportActivation == null ? "TERRAIN VIEWPORT OFF" :
                    viewportActivation.StatusText;
                return;
            }

            if (!TerrainSamplingAvailable())
            {
                StatusText = "TERRAIN DATA UNAVAILABLE";
                AlertLevel = AERISTerrainAlertLevel.None;
                return;
            }

            float range = ResolveDisplayRangeMeters(settings, landing);
            bool trackUp = settings == null || settings.NavigationDisplayTrackUp;
            float heading = trackUp ? ResolveMapHeading(vessel) : 0f;
            if (ShouldStartRebuild(vessel, range, heading, trackUp))
                BeginRebuild(vessel, range, heading, trackUp);

            if (rebuilding) ContinueRebuild(vessel.mainBody);
            EvaluateThreat(vessel, landing);
        }

        void ClearInactiveFlightViewportState(bool discardPublishedGrid)
        {
            bool hadGridWork = hasActiveGrid || rebuilding || pendingValidCount > 0;
            rebuilding = false;
            pendingIndex = 0;
            pendingValidCount = 0;
            sampleBudget = 0f;
            lastSamplerRealtime = 0f;
            if (discardPublishedGrid && hadGridWork)
            {
                hasActiveGrid = false;
                activeBody = string.Empty;
                pendingBody = string.Empty;
                Array.Clear(activeFlags, 0, activeFlags.Length);
                Array.Clear(pendingFlags, 0, pendingFlags.Length);
                gridRevision++;
                nextRebuildAllowedRealtime = 0f;
            }
            AlertLevel = AERISTerrainAlertLevel.None;
            heldAlert = AERISTerrainAlertLevel.None;
            alertHoldUntil = 0f;
            MinimumPredictedClearanceMeters = float.PositiveInfinity;
            MinimumPredictedClearanceSeconds = float.PositiveInfinity;
        }

        internal bool TryGetCell(int column, int row, out float elevationMeters, out bool water)
        {
            elevationMeters = 0f;
            water = false;
            if (!hasActiveGrid || column < 0 || column >= activeColumns ||
                row < 0 || row >= activeRows) return false;
            int index = row * activeColumns + column;
            if (activeFlags[index] == 0) return false;
            elevationMeters = activeElevation[index];
            water = activeFlags[index] == 2;
            return true;
        }

        internal void CellLocalMeters(int column, int row, out float rightMeters,
            out float forwardMeters)
        {
            float u = activeColumns <= 1 ? 0.5f : column / (float)(activeColumns - 1);
            float v = activeRows <= 1 ? 0.75f : row / (float)(activeRows - 1);
            float range = Mathf.Max(1000f, RangeMeters);
            rightMeters = (u - 0.5f) * 1.30f * range;
            forwardMeters = (0.75f - v) * range;
        }

        // CP3 Gate 4A Compile Hotfix 1: the historical grid-snapshot API belonged
        // exclusively to the retired CPU terrain raster worker. GPU-only presentation
        // consumes immutable tile render-ready height fields instead, so no compiled
        // source may depend on the retired CPU-raster grid-snapshot contract.

        internal static float ResolveDisplayRangeMeters(AERISSettings settings,
            AERISLandingFoundation landing)
        {
            float manual = settings == null ? 20000f :
                AERISSettings.NormalizeNavigationRange(
                    settings.NavigationDisplayManualRangeMeters);
            if (settings == null || !settings.NavigationDisplayAutoRange) return manual;
            AERISRunwayObservation observation = landing == null ? null : landing.Observation;
            if (landing != null && landing.Armed && observation != null && observation.Valid)
            {
                float value = (float)Math.Max(5000.0,
                    Math.Min(120000.0, observation.DistanceToThresholdMeters * 1.20));
                return SnapRangeUp(value);
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            float speed = vessel != null && Finite(vessel.srfSpeed) ? (float)vessel.srfSpeed : 0f;
            return speed >= 1000f ? 100000f : speed >= 400f ? 50000f :
                speed >= 160f ? 20000f : speed >= 60f ? 10000f : 5000f;
        }

        internal static float SnapRangeUp(float meters)
        {
            float[] ranges = AERISSettings.NavigationDisplayRangeStepsMeters;
            for (int i = 0; i < ranges.Length; i++) if (meters <= ranges[i]) return ranges[i];
            return ranges[ranges.Length - 1];
        }

        bool ShouldStartRebuild(Vessel vessel, float range, float heading, bool trackUp)
        {
            if (rebuilding) return false;
            if (!hasActiveGrid) return true;
            if (Time.realtimeSinceStartup < nextRebuildAllowedRealtime) return false;
            if (!string.Equals(activeBody, vessel.mainBody.name, StringComparison.OrdinalIgnoreCase)) return true;
            if (performance != null && activeProfileRevision != performance.ProfileRevision) return true;
            AERISTerrainPerformanceProfile profile = performance == null ? null : performance.ActiveProfile;
            if (profile != null && (activeColumns != profile.GridColumns || activeRows != profile.GridRows)) return true;
            double moved = SurfaceDistanceMeters(vessel.mainBody, activeLatitude, activeLongitude,
                vessel.latitude, vessel.longitude);
            // A complete refresh is deliberately coarse-grained. The active grid remains
            // usable while the next grid is assembled, avoiding continuous PQS churn.
            if (!Finite(moved) || moved >= Math.Max(250.0, range * 0.08)) return true;
            if (Math.Abs(activeRangeMeters - range) > Math.Max(1f, range * 0.01f)) return true;
            if (activeTrackUp != trackUp) return true;
            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(activeHeadingDeg, heading)) >= 10f) return true;
            return false;
        }

        void BeginRebuild(Vessel vessel, float range, float heading, bool trackUp)
        {
            rebuilding = true;
            pendingIndex = 0;
            pendingValidCount = 0;
            pendingBody = vessel.mainBody.name;
            pendingLatitude = vessel.latitude;
            pendingLongitude = vessel.longitude;
            pendingRangeMeters = Mathf.Clamp(range, 1000f, 320000f);
            pendingHeadingDeg = heading;
            pendingTrackUp = trackUp;
            AERISTerrainPerformanceProfile profile = performance == null ? null : performance.ActiveProfile;
            pendingColumns = profile == null ? 13 : Mathf.Clamp(profile.GridColumns, 3, MaximumGridColumns);
            pendingRows = profile == null ? 9 : Mathf.Clamp(profile.GridRows, 3, MaximumGridRows);
            pendingProfileRevision = performance == null ? 0 : performance.ProfileRevision;
            Array.Clear(pendingFlags, 0, pendingFlags.Length);
            sampleBudget = 0f;
            lastSamplerRealtime = Time.realtimeSinceStartup;
            StatusText = hasActiveGrid ? "TERRAIN DATA UPDATING" : "TERRAIN DATA LOADING";
        }

        void ContinueRebuild(CelestialBody body)
        {
            if (body == null || !string.Equals(body.name, pendingBody,
                StringComparison.OrdinalIgnoreCase))
            {
                rebuilding = false;
                return;
            }

            float now = Time.realtimeSinceStartup;
            AERISTerrainPerformanceProfile profile = performance == null ? null : performance.ActiveProfile;
            float queriesPerSecond = profile == null ? 25f : Mathf.Clamp(profile.PqsQueriesPerSecond, 1f, 300f);
            int maximumPerFrame = profile == null ? 1 : Mathf.Clamp(profile.MaximumSamplesPerFrame, 1, 8);
            float elapsed = lastSamplerRealtime > 0f ? Mathf.Clamp(now - lastSamplerRealtime, 0f, 0.25f) : 0f;
            lastSamplerRealtime = now;
            sampleBudget = Mathf.Min(maximumPerFrame * 2f, sampleBudget + elapsed * queriesPerSecond);
            int samplesThisFrame = Mathf.Min(maximumPerFrame, Mathf.FloorToInt(sampleBudget));
            if (samplesThisFrame <= 0) return;
            sampleBudget -= samplesThisFrame;

            int pendingCellCount = pendingColumns * pendingRows;
            int end = Math.Min(pendingCellCount, pendingIndex + samplesThisFrame);
            Stopwatch sampleStopwatch = Stopwatch.StartNew();
            for (; pendingIndex < end; pendingIndex++)
            {
                int row = pendingIndex / pendingColumns;
                int column = pendingIndex - row * pendingColumns;
                float right, forward;
                PendingCellLocalMeters(column, row, out right, out forward);
                double east, north;
                RotateDisplayToEastNorth(right, forward, pendingHeadingDeg,
                    pendingTrackUp, out east, out north);
                double latitude, longitude;
                OffsetLatLon(body, pendingLatitude, pendingLongitude, east, north,
                    out latitude, out longitude);
                double elevation;
                if (TrySampleTerrainAsl(body, latitude, longitude, out elevation))
                {
                    pendingElevation[pendingIndex] = (float)elevation;
                    pendingFlags[pendingIndex] = body.ocean && elevation <= 1.0 ? (byte)2 : (byte)1;
                    pendingValidCount++;
                }
                else
                {
                    pendingElevation[pendingIndex] = 0f;
                    pendingFlags[pendingIndex] = 0;
                }
            }
            sampleStopwatch.Stop();
            if (performance != null && samplesThisFrame > 0)
                performance.RecordPqsSampleCost((float)sampleStopwatch.Elapsed.TotalMilliseconds /
                    samplesThisFrame);
            if (pendingIndex < pendingColumns * pendingRows) return;
            if (pendingValidCount == 0)
            {
                rebuilding = false;
                nextRebuildAllowedRealtime = Time.realtimeSinceStartup + 2f;
                StatusText = "TERRAIN DATA DEGRADED";
                return;
            }

            int activeCellCount = pendingColumns * pendingRows;
            Array.Copy(pendingElevation, activeElevation, activeCellCount);
            Array.Copy(pendingFlags, activeFlags, activeCellCount);
            if (activeCellCount < activeFlags.Length)
                Array.Clear(activeFlags, activeCellCount, activeFlags.Length - activeCellCount);
            activeBody = pendingBody;
            activeLatitude = pendingLatitude;
            activeLongitude = pendingLongitude;
            activeRangeMeters = pendingRangeMeters;
            activeHeadingDeg = pendingHeadingDeg;
            activeTrackUp = pendingTrackUp;
            activeColumns = pendingColumns;
            activeRows = pendingRows;
            activeProfileRevision = pendingProfileRevision;
            hasActiveGrid = true;
            gridRevision++;
            rebuilding = false;
            float refreshSeconds = profile == null ? 2f :
                Mathf.Max(profile.MinimumGridRefreshSeconds,
                    1f / Mathf.Max(0.5f, performance.EffectiveTerrainFps));
            nextRebuildAllowedRealtime = Time.realtimeSinceStartup + refreshSeconds;
            StatusText = pendingValidCount < activeCellCount * 4 / 5 ?
                "TERRAIN DATA DEGRADED" : "TERRAIN DATA READY";
        }

        void PendingCellLocalMeters(int column, int row, out float rightMeters,
            out float forwardMeters)
        {
            float u = column / (float)(pendingColumns - 1);
            float v = row / (float)(pendingRows - 1);
            rightMeters = (u - 0.5f) * 1.30f * pendingRangeMeters;
            forwardMeters = (0.75f - v) * pendingRangeMeters;
        }

        void EvaluateThreat(Vessel vessel, AERISLandingFoundation landing)
        {
            AERISTerrainAlertLevel candidate = AERISTerrainAlertLevel.None;
            MinimumPredictedClearanceMeters = float.PositiveInfinity;
            MinimumPredictedClearanceSeconds = float.PositiveInfinity;
            bool airborne = vessel != null && !vessel.LandedOrSplashed &&
                vessel.situation != Vessel.Situations.PRELAUNCH &&
                vessel.situation != Vessel.Situations.SPLASHED;
            if (!hasActiveGrid || !airborne || GroundSpeedMetersPerSecond < 25f)
            {
                SetAlert(candidate);
                return;
            }

            // A stabilized runway approach uses a narrower nuisance-rejection envelope,
            // never a blanket terrain-alert inhibit. A safe 3-degree path may intentionally
            // converge to roughly the threshold crossing height; rising terrain above that
            // path must still produce TERRAIN AHEAD or PULL UP on the FDI.
            AERISRunwayObservation observation = landing == null ? null : landing.Observation;
            bool stabilizedApproach = landing != null && landing.Armed && observation != null &&
                observation.Valid && observation.LocalizerGeometryEligible &&
                observation.GlidePathGeometryEligible &&
                Math.Abs(observation.CrossTrackMeters) <= 180.0 &&
                observation.GlidePathErrorMeters >= -45.0 &&
                observation.GlidePathErrorMeters <= 75.0 &&
                observation.ApproachDistanceMeters <= 18000.0;

            float speed = Mathf.Max(25f, GroundSpeedMetersPerSecond);
            float pullUpSeconds = stabilizedApproach ? 14f : 20f;
            float pullUpClearance = stabilizedApproach ? -45f : 0f;
            float aheadSeconds = stabilizedApproach ? 24f : 35f;
            float aheadClearance = stabilizedApproach ? 0f : 150f;
            for (int row = 0; row < activeRows; row++)
            {
                for (int column = 0; column < activeColumns; column++)
                {
                    int index = row * activeColumns + column;
                    if (activeFlags[index] == 0 || activeFlags[index] == 2) continue;
                    float right, forward;
                    CellLocalMeters(column, row, out right, out forward);
                    if (forward <= 100f) continue;
                    float corridor = Mathf.Max(180f, forward * 0.10f);
                    if (Mathf.Abs(right) > corridor) continue;
                    float seconds = forward / speed;
                    if (seconds > 60f) continue;
                    float projectedAltitude = AircraftAltitudeAslMeters +
                        Mathf.Clamp(VerticalSpeedMetersPerSecond, -100f, 60f) * seconds;
                    float clearance = projectedAltitude - activeElevation[index];
                    if (clearance < MinimumPredictedClearanceMeters)
                    {
                        MinimumPredictedClearanceMeters = clearance;
                        MinimumPredictedClearanceSeconds = seconds;
                    }
                    if (seconds <= pullUpSeconds && clearance <= pullUpClearance)
                        candidate = AERISTerrainAlertLevel.PullUp;
                    else if (candidate < AERISTerrainAlertLevel.TerrainAhead &&
                        seconds <= aheadSeconds && clearance <= aheadClearance)
                        candidate = AERISTerrainAlertLevel.TerrainAhead;
                }
            }
            bool waterBelowAircraft = IsAircraftCellWater();
            if (!stabilizedApproach && !waterBelowAircraft &&
                candidate == AERISTerrainAlertLevel.None &&
                RadarAltitudeMeters <= 120f && VerticalSpeedMetersPerSecond <= -3f)
                candidate = AERISTerrainAlertLevel.Terrain;
            SetAlert(candidate);
        }

        bool IsAircraftCellWater()
        {
            if (!hasActiveGrid) return false;
            int column = activeColumns / 2;
            int row = Mathf.Clamp(Mathf.RoundToInt(0.75f * (activeRows - 1)), 0, activeRows - 1);
            int index = row * activeColumns + column;
            return index >= 0 && index < activeFlags.Length && activeFlags[index] == 2;
        }

        void SetAlert(AERISTerrainAlertLevel candidate)
        {
            float now = Time.realtimeSinceStartup;
            if (candidate > heldAlert)
            {
                heldAlert = candidate;
                alertHoldUntil = now + 1.5f;
            }
            else if (candidate == heldAlert && candidate != AERISTerrainAlertLevel.None)
                alertHoldUntil = now + 1.0f;
            else if (candidate < heldAlert && now >= alertHoldUntil)
                heldAlert = candidate;
            AlertLevel = heldAlert;
        }

        // KSP 1.12.5 Vessel has no heading member. Resolve the moving-map orientation
        // geometrically from surface track, with control-point heading as the low-speed fallback.
        // This keeps TERRAIN and ND on one KSP-compatible source and avoids compile-time binding
        // to a non-existent Vessel.heading API.
        internal static float ResolveMapHeading(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null) return 0f;
            try
            {
                Vector3d upD = (vessel.CoM - vessel.mainBody.position).normalized;
                Vector3 up = (Vector3)upD;
                Vector3 north = (Vector3)Vector3d.Exclude(upD, vessel.mainBody.RotationAxis);
                if (up.sqrMagnitude < 0.0001f || north.sqrMagnitude < 0.0004f) return 0f;

                // TRACK UP uses the horizontal surface-velocity vector whenever it is meaningful.
                Vector3 horizontal = Vector3.ProjectOnPlane((Vector3)vessel.srf_velocity, up);
                if (!Finite(horizontal) || horizontal.sqrMagnitude < 1.0f)
                {
                    Transform reference = vessel.ReferenceTransform;
                    if (reference == null) return 0f;
                    // KSP's active control-frame longitudinal/nose axis is ReferenceTransform.up.
                    horizontal = Vector3.ProjectOnPlane(reference.up, up);
                }
                if (!Finite(horizontal) || horizontal.sqrMagnitude < 0.0004f) return 0f;

                north.Normalize();
                horizontal.Normalize();
                float heading = Mathf.Repeat(Vector3.SignedAngle(north, horizontal, up), 360f);
                return Finite(heading) ? heading : 0f;
            }
            catch { return 0f; }
        }

        internal static bool TerrainSamplingAvailableShared()
        {
            return TerrainSamplingAvailable();
        }

        static bool TerrainSamplingAvailable()
        {
            if (terrainMethodResolved) return terrainAltitudeMethod != null;
            terrainMethodResolved = true;
            try
            {
                MethodInfo[] methods = typeof(CelestialBody).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (!string.Equals(method.Name, "TerrainAltitude",
                        StringComparison.Ordinal)) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    bool two = parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(double) &&
                        parameters[1].ParameterType == typeof(double);
                    bool three = parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(double) &&
                        parameters[1].ParameterType == typeof(double) &&
                        parameters[2].ParameterType == typeof(bool);
                    if (!two && !three) continue;
                    terrainAltitudeMethod = method;
                    try
                    {
                        // Open-instance delegates avoid MethodInfo.Invoke and per-sample
                        // object-array allocation on the normal KSP 1.12.5 path.
                        if (two)
                            terrainAltitude2 = (TerrainAltitude2)Delegate.CreateDelegate(
                                typeof(TerrainAltitude2), method);
                        else
                            terrainAltitude3 = (TerrainAltitude3)Delegate.CreateDelegate(
                                typeof(TerrainAltitude3), method);
                    }
                    catch
                    {
                        terrainAltitude2 = null;
                        terrainAltitude3 = null;
                    }
                    break;
                }
            }
            catch
            {
                terrainAltitudeMethod = null;
                terrainAltitude2 = null;
                terrainAltitude3 = null;
            }
            return terrainAltitudeMethod != null;
        }

        bool TrySampleTerrainAsl(CelestialBody body, double latitude,
            double longitude, out double terrainAsl)
        {
            bool ok = TrySampleTerrainAslShared(body, latitude, longitude, out terrainAsl);
            if (!ok && Time.realtimeSinceStartup >= nextFaultLogRealtime)
                nextFaultLogRealtime = Time.realtimeSinceStartup + 10f;
            return ok;
        }

        internal static bool TrySampleTerrainAslShared(CelestialBody body,
            double latitude, double longitude, out double terrainAsl)
        {
            terrainAsl = 0.0;
            if (body == null || !Finite(latitude) || !Finite(longitude) ||
                !TerrainSamplingAvailable()) return false;
            try
            {
                if (terrainAltitude2 != null)
                    terrainAsl = terrainAltitude2(body, latitude, longitude);
                else if (terrainAltitude3 != null)
                    terrainAsl = terrainAltitude3(body, latitude, longitude, false);
                else
                {
                    ParameterInfo[] parameters = terrainAltitudeMethod.GetParameters();
                    object raw;
                    if (parameters.Length == 2)
                    {
                        reflectionArgs2[0] = latitude;
                        reflectionArgs2[1] = longitude;
                        raw = terrainAltitudeMethod.Invoke(body, reflectionArgs2);
                    }
                    else
                    {
                        reflectionArgs3[0] = latitude;
                        reflectionArgs3[1] = longitude;
                        reflectionArgs3[2] = false;
                        raw = terrainAltitudeMethod.Invoke(body, reflectionArgs3);
                    }
                    terrainAsl = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                }
                return Finite(terrainAsl);
            }
            catch (Exception ex)
            {
                if (Time.realtimeSinceStartup >= nextSharedFaultLogRealtime)
                {
                    nextSharedFaultLogRealtime = Time.realtimeSinceStartup + 10f;
                    AERISLogger.Warn("[TERRAIN] sample fault isolated from flight control: " +
                        ex.GetType().Name);
                }
                return false;
            }
        }

        static void RotateDisplayToEastNorth(double right, double forward,
            double headingDeg, bool trackUp, out double east, out double north)
        {
            if (!trackUp) { east = right; north = forward; return; }
            double heading = headingDeg * Math.PI / 180.0;
            east = right * Math.Cos(heading) + forward * Math.Sin(heading);
            north = -right * Math.Sin(heading) + forward * Math.Cos(heading);
        }

        static void OffsetLatLon(CelestialBody body, double originLatDeg,
            double originLonDeg, double eastMeters, double northMeters,
            out double latitudeDeg, out double longitudeDeg)
        {
            double radius = Math.Max(1.0, body == null ? 1.0 : body.Radius);
            double distance = Math.Sqrt(eastMeters * eastMeters + northMeters * northMeters);
            if (distance < 0.001)
            {
                latitudeDeg = originLatDeg;
                longitudeDeg = originLonDeg;
                return;
            }
            double bearing = Math.Atan2(eastMeters, northMeters);
            double angular = distance / radius;
            double lat1 = originLatDeg * Math.PI / 180.0;
            double lon1 = originLonDeg * Math.PI / 180.0;
            double sinLat1 = Math.Sin(lat1);
            double cosLat1 = Math.Cos(lat1);
            double sinAngular = Math.Sin(angular);
            double cosAngular = Math.Cos(angular);
            double lat2 = Math.Asin(sinLat1 * cosAngular +
                cosLat1 * sinAngular * Math.Cos(bearing));
            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * sinAngular * cosLat1,
                cosAngular - sinLat1 * Math.Sin(lat2));
            latitudeDeg = lat2 * 180.0 / Math.PI;
            longitudeDeg = NormalizeLongitude(lon2 * 180.0 / Math.PI);
        }

        static double SurfaceDistanceMeters(CelestialBody body, double lat1Deg,
            double lon1Deg, double lat2Deg, double lon2Deg)
        {
            if (body == null) return double.NaN;
            double lat1 = lat1Deg * Math.PI / 180.0;
            double lat2 = lat2Deg * Math.PI / 180.0;
            double dLat = lat2 - lat1;
            double dLon = NormalizeLongitude(lon2Deg - lon1Deg) * Math.PI / 180.0;
            double sinLat = Math.Sin(dLat * 0.5);
            double sinLon = Math.Sin(dLon * 0.5);
            double a = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
            double angle = 2.0 * Math.Atan2(Math.Sqrt(Math.Max(0.0, a)),
                Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            return body.Radius * angle;
        }

        static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        internal void Dispose()
        {
            if (displayTiles != null) displayTiles.Dispose();
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
