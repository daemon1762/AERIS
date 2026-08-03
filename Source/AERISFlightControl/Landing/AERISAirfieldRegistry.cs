using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISAirfieldRegistry : IDisposable
    {
        const string RelativeDirectory = "GameData/AERISFlightControl/Airfields";
        readonly AERISSettings settings;
        readonly AERISMapDramCache mapDramCache;
        readonly bool buildOnly;
        readonly AERISRunwayCertificationCache cache;
        readonly AERISRunwayCertificationWorker worker;
        readonly AERISRunwayWitnessLibrary witnessLibrary;
        readonly HashSet<string> revokedDirectionIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<AERISAirfieldDefinition> airfields = new List<AERISAirfieldDefinition>();
        AERISAirfieldRegistry stagedRegistry;
        List<AERISProviderFacilityRecord> stagedRecords;
        AERISRunwaySurveyCatalog stagedCatalog;
        string[] discoveryFiles;
        int discoveryFileIndex;
        int discoveryRecordIndex;
        int discoveryStep;
        int surveyIndex;
        AERISProviderFacilityRecord activeSurveyRecord;
        AERISRunwaySurveySnapshot activeSurveySnapshot;
        AERISProviderFacilityRecord activeCaptureRecord;
        AERISRunwaySnapshotBuilder.Capture activeCapture;
        long activeCaptureSequence;
        long activeCaptureStartedTimestamp;
        long activeSurveySequence;
        long activeSurveyStartedTimestamp;
        bool refreshRequested;
        bool pendingRefresh;
        bool startupRequested;
        bool startupComplete;
        bool disposed;
        bool approachFrozen;
        string refreshCause = "NONE";
        long generation;
        long sequence;
        long databaseRevision;
        long stagedDatabaseRevision;
        long selectionRevision;
        int measuredCount;
        int cacheHitCount;
        int failedCount;
        int pendingCount;
        int revalidationCount;
        int provisionalCount;
        int stockCount;
        int dlcDefinedCount;
        int dlcDetectedCount;
        int kkCount;
        int sleCount;
        int validatedCount;
        int runwayCount;
        int registeredApproachCount;
        int snapshotSliceCount;
        int snapshotHardOverrunCount;
        double snapshotMaximumSliceMilliseconds;
        DateTime lastLoadUtc;

        internal AERISAirfieldRegistry(AERISSettings settings)
            : this(settings, null, false)
        {
        }

        internal AERISAirfieldRegistry(AERISSettings settings,
            AERISMapDramCache mapDramCache)
            : this(settings, mapDramCache, false)
        {
        }

        AERISAirfieldRegistry(AERISSettings settings, bool buildOnly)
            : this(settings, null, buildOnly)
        {
        }

        AERISAirfieldRegistry(AERISSettings settings,
            AERISMapDramCache mapDramCache, bool buildOnly)
        {
            this.settings = settings;
            this.mapDramCache = mapDramCache;
            this.buildOnly = buildOnly;
            if (!buildOnly)
            {
                cache = new AERISRunwayCertificationCache();
                worker = new AERISRunwayCertificationWorker();
                witnessLibrary = new AERISRunwayWitnessLibrary();
            }
        }

        internal IList<AERISAirfieldDefinition> Airfields
        {
            get
            {
                return mapDramCache == null ? airfields.AsReadOnly() :
                    mapDramCache.SnapshotAirfields();
            }
        }
        internal int Count { get { return Airfields.Count; } }
        internal int SelectedAirfieldIndex { get; private set; } = -1;
        internal int SelectedDirectionIndex { get; private set; } = -1;
        internal string Status { get; private set; } = "NOT LOADED";
        internal string KspProviderStatus { get; private set; } = "NOT SCANNED";
        internal string KerbalKonstructsProviderStatus { get; private set; } = "NOT SCANNED";
        internal string ExpansionStatus { get { return AERISExpansionStatus.ExpansionSummary; } }
        internal string DlcRunwayStatus
        {
            get { return AERISExpansionStatus.DlcRunwaySummary(dlcDetectedCount, dlcDefinedCount); }
        }

        // Candidate 10 presentation authority: configured/cached non-stock runway data
        // remains in the registry as evidence, but it is not a live UI/ND/LAND target
        // unless the corresponding provider exposed that facility in this session.
        // DLC is the deliberate exception: an installed expansion may be shown as a
        // non-selectable placeholder even when the current save/session has not exposed
        // its runtime facility yet. This keeps the database baseline without inventing
        // an airport that is not actually installed.
        internal bool IsAirfieldPresentationAvailable(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return false;
            switch (airfield.Source)
            {
                case AERISAirfieldSource.Stock:
                    return true;
                case AERISAirfieldSource.Dlc:
                    if (airfield.ProviderDetected) return true;
                    string marker = ((airfield.SourceMod ?? string.Empty) + " " +
                        (airfield.Id ?? string.Empty) + " " +
                        (airfield.DisplayName ?? string.Empty)).ToLowerInvariant();
                    if (marker.Contains("makinghistory") || marker.Contains("dessert") ||
                        marker.Contains("woomerang"))
                        return AERISExpansionStatus.MakingHistoryInstalled ||
                            AERISExpansionStatus.MakingHistoryLoaded;
                    if (marker.Contains("serenity") || marker.Contains("breakingground"))
                        return AERISExpansionStatus.BreakingGroundInstalled ||
                            AERISExpansionStatus.BreakingGroundLoaded;
                    return false;
                case AERISAirfieldSource.KerbalKonstructs:
                case AERISAirfieldSource.StockLaunchsitesExpansion:
                    return airfield.ProviderDetected;
                case AERISAirfieldSource.UserCfg:
                    return true;
                default:
                    return airfield.ProviderDetected;
            }
        }

        internal bool IsDlcRunwayPresentationPlaceholder(AERISAirfieldDefinition airfield)
        {
            return airfield != null && airfield.Source == AERISAirfieldSource.Dlc &&
                airfield.FacilityKind == AERISFacilityKind.Runway &&
                IsAirfieldPresentationAvailable(airfield) && airfield.DirectionCount == 0;
        }

        internal string DlcRunwayPresentationText(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return "DLC RUNWAY UNAVAILABLE";
            if (airfield.ProviderDetected) return "DLC RUNTIME DETECTED — GEOMETRY REQUIRED";
            if (AERISExpansionStatus.MakingHistoryLoaded)
                return "DLC LOADED — SAVE-LOCKED / NOT EXPOSED";
            if (AERISExpansionStatus.MakingHistoryInstalled)
                return "DLC INSTALLED — SESSION RESTART / SAVE EXPOSURE REQUIRED";
            return "DLC NOT INSTALLED";
        }
        internal string RunwaySurveyStatus { get; private set; } = "NOT LOADED";
        internal string PhysicalRunwayStatus { get; private set; } = "NOT FEDERATED";
        internal AERISAirfieldReloadState ReloadState { get; private set; } =
            AERISAirfieldReloadState.Idle;
        internal string ReloadStateText { get { return ReloadState.ToString().ToUpperInvariant(); } }
        internal bool ApproachFrozen { get { return approachFrozen; } }
        internal bool HasStagedDatabase { get { return ReloadState == AERISAirfieldReloadState.Staged; } }
        internal bool RefreshPending { get { return pendingRefresh; } }
        internal long DatabaseRevision { get { return databaseRevision; } }
        internal long MapDramRevision
        {
            get
            {
                return mapDramCache == null ? 0L :
                    mapDramCache.SnapshotTelemetry().AirfieldRevision;
            }
        }
        internal bool TryGetMapAirfield(string stableId,
            out AERISAirfieldDefinition airfield)
        {
            airfield = null;
            return mapDramCache != null &&
                mapDramCache.TryGetAirfieldView(stableId, out airfield);
        }

        internal bool TryGetMapRunway(string stableId,
            out AERISRunwayDefinition runway)
        {
            runway = null;
            return mapDramCache != null &&
                mapDramCache.TryGetRunwayView(stableId, out runway);
        }

        internal bool TryGetMapDirection(string stableId,
            out AERISRunwayDirectionDefinition direction)
        {
            direction = null;
            return mapDramCache != null &&
                mapDramCache.TryGetDirectionView(stableId, out direction);
        }

        internal long SelectionRevision { get { return selectionRevision; } }
        internal long StagedDatabaseRevision { get { return stagedDatabaseRevision; } }
        internal int RunwayCount { get { return runwayCount; } }
        internal int RegisteredApproachCount { get { return registeredApproachCount; } }
        internal int WorkerQueueDepth { get { return worker == null ? 0 : worker.QueueDepth; } }
        internal long WorkerReplaced { get { return worker == null ? 0L : worker.Replaced; } }
        internal long WorkerStale { get { return worker == null ? 0L : worker.Stale; } }
        internal string RunwayWitnessStatus
        {
            get { return witnessLibrary == null ? "DISABLED" : witnessLibrary.Status; }
        }
        internal string RunwayCalibrationStatus
        {
            get { return witnessLibrary == null ? "DISABLED" : witnessLibrary.CalibrationStatus; }
        }
        internal string LastLoadText
        {
            get
            {
                return lastLoadUtc == default(DateTime) ? "NEVER" :
                    lastLoadUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'",
                        System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        internal string SourceSummary
        {
            get
            {
                return "RWY " + PresentationRunwayCount() + " / APP " +
                    PresentationApproachCount() + " | STOCK " +
                    PresentationAirfieldCount(AERISAirfieldSource.Stock) +
                    " | DLC RWY " + PresentationAirfieldCount(AERISAirfieldSource.Dlc) +
                    " | KK " + PresentationAirfieldCount(AERISAirfieldSource.KerbalKonstructs) +
                    " | SLE " + PresentationAirfieldCount(AERISAirfieldSource.StockLaunchsitesExpansion) +
                    " | CERT APP " +
                    CountPresentationApproaches(AERISRunwayCertificationState.Certified);
            }
        }

        internal string CertificationSummary
        {
            get
            {
                return "REGISTERED " + PresentationRunwayCount() + " RWY / " +
                    PresentationApproachCount() + " APP | CERTIFIED " +
                    CountPresentationRunways(AERISRunwayCertificationState.Certified) +
                    " RWY / " + CountPresentationApproaches(AERISRunwayCertificationState.Certified) + " APP | FAILED " +
                    CountPresentationRunways(AERISRunwayCertificationState.Failed) + " RWY / " +
                    CountPresentationApproaches(AERISRunwayCertificationState.Failed) +
                    " APP | PENDING " +
                    CountPresentationRunways(AERISRunwayCertificationState.Pending) + " RWY / " +
                    CountPresentationApproaches(AERISRunwayCertificationState.Pending) +
                    " APP | REVALIDATE " +
                    CountPresentationRunways(AERISRunwayCertificationState.Revalidation) + " RWY / " +
                    CountPresentationApproaches(AERISRunwayCertificationState.Revalidation) +
                    " APP | PROVISIONAL " +
                    CountPresentationRunways(AERISRunwayCertificationState.Provisional) + " RWY / " +
                    CountPresentationApproaches(AERISRunwayCertificationState.Provisional) + " APP";
            }
        }

        internal int CertifiedApproachCount
        {
            get { return CountApproaches(AERISRunwayCertificationState.Certified); }
        }
        internal int FailedApproachCount
        {
            get { return CountApproaches(AERISRunwayCertificationState.Failed); }
        }
        internal int PendingApproachCount
        {
            get { return CountApproaches(AERISRunwayCertificationState.Pending); }
        }
        internal int RevalidationApproachCount
        {
            get { return CountApproaches(AERISRunwayCertificationState.Revalidation); }
        }
        internal int ProvisionalApproachCount
        {
            get { return CountApproaches(AERISRunwayCertificationState.Provisional); }
        }

        internal AERISAirfieldDefinition SelectedAirfield
        {
            get
            {
                AERISAirfieldDefinition indexed = At(SelectedAirfieldIndex);
                if (indexed == null || mapDramCache == null) return indexed;
                AERISAirfieldDefinition resolved;
                return TryGetMapAirfield(indexed.StableId, out resolved) ?
                    resolved : null;
            }
        }

        internal AERISRunwayDirectionDefinition SelectedDirection
        {
            get
            {
                AERISAirfieldDefinition airfield = SelectedAirfield;
                AERISRunwayDirectionDefinition indexed =
                    SelectableDirectionAt(airfield, SelectedDirectionIndex);
                if (indexed == null || mapDramCache == null) return indexed;
                AERISRunwayDirectionDefinition resolved;
                return TryGetMapDirection(indexed.StableId, out resolved) ?
                    resolved : null;
            }
        }

        internal AERISRunwayDefinition SelectedRunway
        {
            get
            {
                AERISAirfieldDefinition airfield = SelectedAirfield;
                AERISRunwayDirectionDefinition direction = SelectedDirection;
                AERISRunwayDefinition indexed = airfield == null ? null :
                    airfield.RunwayForDirection(direction);
                if (indexed == null || mapDramCache == null) return indexed;
                AERISRunwayDefinition resolved;
                return TryGetMapRunway(indexed.StableId, out resolved) ?
                    resolved : null;
            }
        }

        internal int SelectedDirectionCount
        {
            get { return SelectableDirectionCount(SelectedAirfield); }
        }

        // Compatibility entry point.  Startup and UI code use the explicit request methods.
        internal void Reload()
        {
            RequestManualReload();
        }

        internal void RequestStartupLoad()
        {
            if (buildOnly || disposed || startupRequested) return;
            startupRequested = true;
            refreshCause = "STARTUP";
            refreshRequested = true;
            Status = "STARTUP LOAD REQUESTED";
        }

        internal void RequestManualReload()
        {
            if (buildOnly || disposed) return;
            if (refreshRequested || IsReloadActive())
            {
                pendingRefresh = true;
                Status = "MANUAL RELOAD COALESCED — ONE PENDING";
                return;
            }
            refreshCause = "MANUAL";
            refreshRequested = true;
            Status = "MANUAL RELOAD REQUESTED";
        }

        internal void RefreshDynamicProviders()
        {
            // Scene changes do not trigger a full rescan.  This method only supplies the
            // one startup request if providers were not ready earlier.
            if (!startupRequested) RequestStartupLoad();
        }

        internal void Tick(bool freezeApproachGeometry, bool providerRuntimeReady)
        {
            if (buildOnly || disposed) return;
            approachFrozen = freezeApproachGeometry;
            if (refreshRequested && !IsReloadActive() &&
                CanBeginRefresh(providerRuntimeReady)) BeginRefresh();
            switch (ReloadState)
            {
                case AERISAirfieldReloadState.LoadingCache:
                    AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                        "AIRFIELD_CACHE_LOAD");
                    cache.Load();
                    int purgedAutomaticAuthority = cache.PurgeNonStockAutomaticAuthority();
                    if (purgedAutomaticAuthority > 0)
                        AERISLogger.Warn("[AIRFIELD_AUTHORITY_POLICY] purged " +
                            purgedAutomaticAuthority +
                            " non-stock automatic certification cache record(s); " +
                            "only base-game Stock and USER CALIBRATED A/B may be authority.");
                    ReloadState = AERISAirfieldReloadState.Discovering;
                    Status = "DISCOVERING";
                    break;
                case AERISAirfieldReloadState.Discovering:
                    TickDiscovery();
                    break;
                case AERISAirfieldReloadState.Surveying:
                    TickSurvey();
                    break;
                case AERISAirfieldReloadState.Validating:
                    TickValidation();
                    break;
                case AERISAirfieldReloadState.Staged:
                    if (!approachFrozen) CommitStaged();
                    else Status = "STAGED — APPROACH FREEZE ACTIVE";
                    break;
            }
            if (!IsReloadActive() && !refreshRequested && pendingRefresh)
            {
                pendingRefresh = false;
                refreshCause = "MANUAL-PENDING";
                refreshRequested = true;
            }
        }

        internal AERISAirfieldDefinition At(int index)
        {
            IList<AERISAirfieldDefinition> values = Airfields;
            return index >= 0 && index < values.Count ? values[index] : null;
        }

        internal bool SelectAirfield(int index)
        {
            if (approachFrozen) return false;
            AERISAirfieldDefinition selected = At(index);
            if (selected == null || !IsAirfieldPresentationAvailable(selected) ||
                SelectableDirectionCount(selected) == 0) return false;
            SelectedAirfieldIndex = index;
            SelectedDirectionIndex = SelectableDirectionCount(selected) > 0 ? 0 : -1;
            if (settings != null) settings.LandSelectionExplicitlyCleared = false;
            selectionRevision++;
            PersistSelection();
            return true;
        }

        internal bool SelectDirection(int index)
        {
            if (approachFrozen) return false;
            AERISAirfieldDefinition selected = SelectedAirfield;
            if (selected == null || SelectableDirectionAt(selected, index) == null) return false;
            SelectedDirectionIndex = index;
            if (settings != null) settings.LandSelectionExplicitlyCleared = false;
            selectionRevision++;
            PersistSelection();
            return true;
        }

        internal bool ClearSelection()
        {
            bool changed = SelectedAirfieldIndex >= 0 || SelectedDirectionIndex >= 0;
            SelectedAirfieldIndex = -1;
            SelectedDirectionIndex = -1;
            if (settings != null) settings.LandSelectionExplicitlyCleared = true;
            selectionRevision++;
            PersistSelection();
            AERISLogger.Info("[AIRFIELD_SELECTION] cleared from ND; changed=" + changed +
                "; selectionRevision=" + selectionRevision + ".");
            return true;
        }

        internal string ValidationText(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return "NONE";
            if (airfield.FacilityKind != AERISFacilityKind.Runway)
                return "LAND N/A — " + airfield.FacilityKind.ToString().ToUpperInvariant();
            int certified = SelectableDirectionCount(airfield);
            if (certified > 0) return "CERTIFIED " + certified + "/" +
                VisibleDirectionCount(airfield) + " APP";
            if (HasState(airfield, AERISRunwayCertificationState.Revalidation))
                return "REVALIDATION";
            if (HasState(airfield, AERISRunwayCertificationState.Provisional))
                return "PROVISIONAL — DISPLAY ONLY / LAND ARM INHIBITED";
            if (HasState(airfield, AERISRunwayCertificationState.Pending)) return "PENDING";
            if (HasState(airfield, AERISRunwayCertificationState.Failed)) return "FAILED";
            return airfield.ProviderDetected ? "RUNWAY GEOMETRY REQUIRED" :
                "PROVIDER NOT DETECTED";
        }


        int VisibleDirectionCount(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return 0;
            int count = 0;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                    if (!IsSupersededByUserCalibration(airfield,
                        airfield.Runways[i].Directions[j])) count++;
            return count;
        }

        int PresentationAirfieldCount(AERISAirfieldSource source)
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (airfield == null || airfield.Source != source ||
                    airfield.FacilityKind != AERISFacilityKind.Runway ||
                    !IsAirfieldPresentationAvailable(airfield)) continue;
                count++;
            }
            return count;
        }

        int PresentationRunwayCount()
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (!IsAirfieldPresentationAvailable(airfield) ||
                    airfield.FacilityKind != AERISFacilityKind.Runway) continue;
                if (IsDlcRunwayPresentationPlaceholder(airfield)) { count++; continue; }
                for (int j = 0; j < airfield.Runways.Count; j++)
                    if (airfield.Runways[j] != null && airfield.Runways[j].Directions.Count > 0) count++;
            }
            return count;
        }

        int PresentationApproachCount()
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (!IsAirfieldPresentationAvailable(airfield)) continue;
                for (int j = 0; j < airfield.Runways.Count; j++)
                    count += airfield.Runways[j].Directions.Count;
            }
            return count;
        }

        internal int CountPresentationApproaches(AERISRunwayCertificationState state)
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (!IsAirfieldPresentationAvailable(airfield)) continue;
                for (int j = 0; j < airfield.Runways.Count; j++)
                    for (int k = 0; k < airfield.Runways[j].Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction =
                            airfield.Runways[j].Directions[k];
                        if (IsSupersededByUserCalibration(airfield, direction)) continue;
                        if (EffectiveState(direction) == state) count++;
                    }
            }
            return count;
        }

        internal int CountPresentationRunways(AERISRunwayCertificationState state)
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (!IsAirfieldPresentationAvailable(airfield)) continue;
                for (int j = 0; j < airfield.Runways.Count; j++)
                {
                    bool found = false;
                    for (int k = 0; k < airfield.Runways[j].Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction =
                            airfield.Runways[j].Directions[k];
                        if (IsSupersededByUserCalibration(airfield, direction)) continue;
                        if (EffectiveState(direction) == state) { found = true; break; }
                    }
                    if (found) count++;
                }
                if (state == AERISRunwayCertificationState.Pending &&
                    IsDlcRunwayPresentationPlaceholder(airfield)) count++;
            }
            return count;
        }

        internal int CountApproaches(AERISRunwayCertificationState state)
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
                for (int j = 0; j < airfields[i].Runways.Count; j++)
                    for (int k = 0; k < airfields[i].Runways[j].Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction =
                            airfields[i].Runways[j].Directions[k];
                        if (IsSupersededByUserCalibration(airfields[i], direction)) continue;
                        if (EffectiveState(direction) == state) count++;
                    }
            return count;
        }

        internal int CountRunways(AERISRunwayCertificationState state)
        {
            int count = 0;
            for (int i = 0; i < airfields.Count; i++)
                for (int j = 0; j < airfields[i].Runways.Count; j++)
                {
                    bool found = false;
                    for (int k = 0; k < airfields[i].Runways[j].Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction =
                            airfields[i].Runways[j].Directions[k];
                        if (IsSupersededByUserCalibration(airfields[i], direction)) continue;
                        if (EffectiveState(direction) == state)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) count++;
                }
            return count;
        }

        bool CanBeginRefresh(bool providerRuntimeReady)
        {
            if (providerRuntimeReady) return true;
            Status = refreshCause + " WAITING FOR STABLE FLIGHT PROVIDERS";
            return false;
        }

        void BeginRefresh()
        {
            refreshRequested = false;
            generation++;
            stagedDatabaseRevision = databaseRevision + 1;
            sequence = 0L;
            discoveryFiles = null;
            discoveryFileIndex = 0;
            discoveryRecordIndex = 0;
            discoveryStep = 0;
            surveyIndex = 0;
            activeSurveyRecord = null;
            activeSurveySnapshot = null;
            activeSurveySequence = 0L;
            activeCaptureRecord = null;
            activeCapture = null;
            activeCaptureSequence = 0L;
            activeCaptureStartedTimestamp = 0L;
            activeSurveyStartedTimestamp = 0L;
            measuredCount = cacheHitCount = failedCount = pendingCount = revalidationCount = 0;
            provisionalCount = 0;
            snapshotSliceCount = snapshotHardOverrunCount = 0;
            snapshotMaximumSliceMilliseconds = 0.0;
            stagedRecords = new List<AERISProviderFacilityRecord>();
            AERISExpansionStatus.RequestRefresh();
            stagedRegistry = new AERISAirfieldRegistry(settings, true);
            stagedCatalog = null;
            if (witnessLibrary != null)
            {
                AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                    "AIRFIELD_WITNESS_RELOAD");
                witnessLibrary.Reload();
            }
            worker.Invalidate(generation);
            ReloadState = AERISAirfieldReloadState.LoadingCache;
            Status = "LOADING CACHE";
            AERISLogger.Info("[AIRFIELD_RELOAD] begin cause=" + refreshCause +
                " generation=" + generation + ".");
        }

        void TickDiscovery()
        {
            if (stagedRegistry == null)
            {
                FailReload("STAGED REGISTRY MISSING");
                return;
            }
            try
            {
                if (discoveryStep == 0)
                {
                    string directory = ResolvePath(RelativeDirectory);
                    AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                        "AIRFIELD_DISCOVERY_CREATE_DIRECTORY");
                    Directory.CreateDirectory(directory);
                    AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                        "AIRFIELD_DISCOVERY_ENUMERATE_CFG");
                    discoveryFiles = Directory.GetFiles(directory, "*.cfg",
                        SearchOption.AllDirectories);
                    Array.Sort(discoveryFiles, StringComparer.OrdinalIgnoreCase);
                    discoveryStep = 1;
                    Status = "DISCOVERING CFG 0/" + discoveryFiles.Length;
                    return;
                }
                if (discoveryStep == 1)
                {
                    if (discoveryFileIndex < discoveryFiles.Length)
                    {
                        var parsed = new List<AERISAirfieldDefinition>();
                        AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                            "AIRFIELD_DISCOVERY_PARSE_CFG");
                        AERISAirfieldConfigParser.ParseFile(
                            discoveryFiles[discoveryFileIndex], parsed);
                        for (int i = 0; i < parsed.Count; i++) stagedRegistry.AddConfigured(parsed[i]);
                        discoveryFileIndex++;
                        Status = "DISCOVERING CFG " + discoveryFileIndex + "/" +
                            discoveryFiles.Length;
                        return;
                    }
                    AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                        "AIRFIELD_DISCOVERY_LOAD_SURVEY_CATALOG");
                    stagedCatalog = AERISRunwaySurveyCatalog.Load(discoveryFiles);
                    discoveryStep = 2;
                    return;
                }
                if (discoveryStep == 2)
                {
                    AERISKerbalKonstructsProvider.Collect(stagedRecords, out string kkStatus);
                    stagedRegistry.KerbalKonstructsProviderStatus = kkStatus;
                    discoveryStep = 3;
                    Status = "DISCOVERING KSP FACILITIES";
                    return;
                }
                if (discoveryStep == 3)
                {
                    AERISKspFacilityProvider.Collect(stagedRecords, out string kspStatus);
                    stagedRegistry.KspProviderStatus = kspStatus;
                    if (stagedCatalog != null)
                        for (int i = 0; i < stagedRecords.Count; i++)
                            if (stagedRecords[i] != null)
                                stagedRecords[i].SurveyDefinition =
                                    stagedCatalog.MatchPhysical(stagedRecords[i]);
                    AERISPhysicalRunwayMergeSummary physicalSummary;
                    stagedRecords = AERISPhysicalRunwayIdentity.Canonicalize(stagedRecords,
                        out physicalSummary);
                    stagedRegistry.PhysicalRunwayStatus = physicalSummary.StatusText;
                    int compactedCertified = 0;
                    int compactedFailures = 0;
                    if (cache != null) cache.CompactPhysicalAliases(stagedRecords,
                        out compactedCertified, out compactedFailures);
                    AERISLogger.Info("[PHYSICAL_RUNWAY] cause=" + refreshCause +
                        " generation=" + generation + "; " + physicalSummary.StatusText +
                        "; identitySignature=" + physicalSummary.IdentitySignature +
                        "; cacheCompacted=" + compactedCertified + "/" +
                        compactedFailures + ".");
                    int providerRunwayCount;
                    string providerGeometrySignature;
                    string providerSignature = ProviderSnapshotSignature(stagedRecords,
                        out providerRunwayCount, out providerGeometrySignature);
                    AERISLogger.Info("[AIRFIELD_PROVIDER_SNAPSHOT] cause=" + refreshCause +
                        " generation=" + generation + "; records=" + stagedRecords.Count +
                        "; runways=" + providerRunwayCount + "; signature=" +
                        providerSignature + "; geometrySignature=" +
                        providerGeometrySignature + "; physical=" +
                        physicalSummary.IdentitySignature + "; KSP=" + kspStatus + "; KK=" +
                        stagedRegistry.KerbalKonstructsProviderStatus + ".");
                    discoveryStep = 4;
                    Status = "DISCOVERING PROVIDERS 0/" + stagedRecords.Count;
                    return;
                }
                if (discoveryStep == 4)
                {
                    if (discoveryRecordIndex < stagedRecords.Count)
                    {
                        AERISProviderFacilityRecord record = stagedRecords[discoveryRecordIndex];
                        record.SurveyDefinition = stagedCatalog == null ? null :
                            stagedCatalog.MatchPhysical(record);
                        record.SurveyStatus = record.FacilityKind == AERISFacilityKind.Runway
                            ? "PENDING CONSENSUS SURVEY" : "CLASSIFIED NON-RUNWAY";
                        stagedRegistry.MergeDiscovered(record);
                        discoveryRecordIndex++;
                        Status = "DISCOVERING PROVIDERS " + discoveryRecordIndex + "/" +
                            stagedRecords.Count;
                        return;
                    }
                    stagedRegistry.SortAndCount();
                    ReloadState = AERISAirfieldReloadState.Surveying;
                    Status = "SURVEYING 0/" + stagedRecords.Count;
                    RunwaySurveyStatus = "CONSENSUS SURVEY STARTED";
                }
            }
            catch (Exception ex)
            {
                FailReload("DISCOVERY FAILED: " + ex.GetType().Name + " — " + ex.Message);
            }
        }

        void TickSurvey()
        {
            if (activeCapture != null)
            {
                if (ElapsedSeconds(activeCaptureStartedTimestamp) > 60.0)
                {
                    AERISProviderFacilityRecord timedOut = activeCaptureRecord;
                    activeCapture = null;
                    activeCaptureRecord = null;
                    activeCaptureSequence = 0L;
                    activeCaptureStartedTimestamp = 0L;
                    ApplySnapshotFailure(timedOut, AERISRunwayFailureCode.SurveyTimeout,
                        "MAIN-THREAD SNAPSHOT CAPTURE EXCEEDED 60 SECONDS");
                    return;
                }
                AERISRunwaySurveySnapshot snapshot;
                AERISRunwayFailureCode captureFailure;
                string captureDetail;
                AERISRunwaySnapshotCaptureProgress progress = activeCapture.Tick(
                    out snapshot, out captureFailure, out captureDetail);
                Status = "SNAPSHOTTING " + surveyIndex + "/" + stagedRecords.Count +
                    " — " + activeCapture.Status;
                if (progress == AERISRunwaySnapshotCaptureProgress.Pending) return;
                AERISProviderFacilityRecord record = activeCaptureRecord;
                long captureSequence = activeCaptureSequence;
                snapshotSliceCount += activeCapture.SliceCount;
                snapshotHardOverrunCount += activeCapture.HardOverrunCount;
                snapshotMaximumSliceMilliseconds = Math.Max(
                    snapshotMaximumSliceMilliseconds,
                    activeCapture.MaximumSliceMilliseconds);
                if (activeCapture.HardOverrunCount > 0)
                    AERISLogger.Warn("[AIRFIELD_SNAPSHOT] " +
                        (record == null ? "UNKNOWN" : record.DisplayName) +
                        " exceeded the 1.50 ms component slice " +
                        activeCapture.HardOverrunCount + " time(s); maximum=" +
                        activeCapture.MaximumSliceMilliseconds.ToString("0.000",
                            System.Globalization.CultureInfo.InvariantCulture) + " ms.");
                activeCapture = null;
                activeCaptureRecord = null;
                activeCaptureSequence = 0L;
                activeCaptureStartedTimestamp = 0L;
                if (progress == AERISRunwaySnapshotCaptureProgress.Failed || snapshot == null)
                {
                    ApplySnapshotFailure(record, captureFailure, captureDetail);
                    return;
                }
                SubmitSnapshot(record, snapshot, captureSequence);
                return;
            }

            if (activeSurveySnapshot != null)
            {
                if (ElapsedSeconds(activeSurveyStartedTimestamp) > 30.0)
                {
                    AERISProviderFacilityRecord timedOut = activeSurveyRecord;
                    AERISRunwaySurveySnapshot timedOutSnapshot = activeSurveySnapshot;
                    generation++;
                    worker.Invalidate(generation);
                    activeSurveyRecord = null;
                    activeSurveySnapshot = null;
                    activeSurveyStartedTimestamp = 0L;
                    failedCount++;
                    stagedRegistry.ApplyFailure(timedOut,
                        AERISRunwayCertificationState.Failed,
                        AERISRunwayFailureCode.SurveyTimeout,
                        "WORKER SURVEY EXCEEDED 30 SECONDS");
                    cache.RecordFailure(timedOut, StableRecordId(timedOut),
                        timedOutSnapshot == null ? string.Empty :
                            timedOutSnapshot.InputFingerprint,
                        AERISRunwayCertificationState.Failed,
                        AERISRunwayFailureCode.SurveyTimeout,
                        "WORKER SURVEY EXCEEDED 30 SECONDS");
                    return;
                }
                AERISRunwaySurveyResult completed;
                if (!worker.TryTakeCompleted(generation, out completed)) return;
                AERISProviderFacilityRecord record = activeSurveyRecord;
                AERISRunwaySurveySnapshot snapshot = activeSurveySnapshot;
                activeSurveyRecord = null;
                activeSurveySnapshot = null;
                activeSurveyStartedTimestamp = 0L;
                if (completed.Sequence != activeSurveySequence ||
                    !string.Equals(completed.InputFingerprint, snapshot.InputFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    worker.Invalidate(generation);
                    revalidationCount++;
                    stagedRegistry.ApplyFailure(record,
                        AERISRunwayCertificationState.Revalidation,
                        AERISRunwayFailureCode.MeshFingerprintChanged,
                        "WORKER RESULT INPUT IDENTITY MISMATCH");
                    cache.RecordFailure(record, StableRecordId(record),
                        snapshot.InputFingerprint,
                        AERISRunwayCertificationState.Revalidation,
                        AERISRunwayFailureCode.MeshFingerprintChanged,
                        "WORKER RESULT INPUT IDENTITY MISMATCH");
                    return;
                }


                if (!AutomaticCertificationAllowed(record) &&
                    !snapshot.RunwayWitnessUserCalibrated)
                {
                    pendingCount++;
                    stagedRegistry.ApplyFailure(record,
                        AERISRunwayCertificationState.Pending,
                        AERISRunwayFailureCode.UserCalibrationRequired,
                        "NON-STOCK AUTOMATIC SURVEY RESULT QUARANTINED — MARK A/B MANUALLY");
                    AERISLogger.Warn("[AIRFIELD_AUTHORITY_POLICY] rejected non-stock automatic " +
                        "worker result; source=" + record.Source + "; site=" +
                        (record.ProviderSiteId ?? string.Empty) + ".");
                    return;
                }

                if (completed.State == AERISRunwayCertificationState.Certified ||
                    completed.State == AERISRunwayCertificationState.Provisional)
                {
                    List<AERISRunwayDefinition> resolved;
                    string detail;
                    bool anyCertified = AERISOperationalRunwayResolver.TryResolve(record,
                        snapshot, completed, stagedDatabaseRevision, out resolved, out detail);
                    stagedRegistry.ApplyResolved(record, resolved, detail);
                    if (completed.State == AERISRunwayCertificationState.Provisional)
                    {
                        provisionalCount++;
                        cache.RecordFailure(record, snapshot.StableRecordId,
                            snapshot.InputFingerprint,
                            AERISRunwayCertificationState.Provisional,
                            completed.FailureCode == AERISRunwayFailureCode.None
                                ? AERISRunwayFailureCode.InsufficientEvidence
                                : completed.FailureCode, completed.Detail);
                    }
                    else if (anyCertified)
                    {
                        measuredCount++;
                        AERISAirfieldDefinition cacheAirfield =
                            stagedRegistry.BuildCacheAirfield(record, resolved);
                        cache.Put(snapshot, record, cacheAirfield);
                    }
                    else
                    {
                        pendingCount++;
                        AERISRunwayFailureCode approachFailure =
                            FirstApproachFailure(resolved);
                        cache.RecordFailure(record, snapshot.StableRecordId,
                            snapshot.InputFingerprint,
                            approachFailure == AERISRunwayFailureCode.None
                                ? AERISRunwayCertificationState.Pending
                                : AERISRunwayCertificationState.Failed,
                            approachFailure, detail);
                    }
                }
                else
                {
                    failedCount++;
                    stagedRegistry.ApplyFailure(record,
                        AERISRunwayCertificationState.Failed,
                        completed.FailureCode, completed.Detail);
                    cache.RecordFailure(record, snapshot.StableRecordId,
                        snapshot.InputFingerprint,
                        AERISRunwayCertificationState.Failed,
                        completed.FailureCode, completed.Detail);
                }
                return;
            }

            // At most one facility is snapshotted/classified per frame.  Cache hits,
            // unsupported facilities and failures are deliberately not collapsed into a
            // single while-loop burst, so a large provider table cannot hitch flight.
            if (surveyIndex < stagedRecords.Count)
            {
                AERISProviderFacilityRecord record = stagedRecords[surveyIndex++];
                Status = "SURVEYING " + surveyIndex + "/" + stagedRecords.Count;
                if (record == null || record.FacilityKind != AERISFacilityKind.Runway) return;
                if (stagedRegistry.HasTrustedConfiguredGeometry(record)) return;

                AERISRunwayWitness witness = witnessLibrary == null
                    ? null : witnessLibrary.Match(record);
                if (!AutomaticCertificationAllowed(record) &&
                    (witness == null || !witness.UserCalibrated || !witness.IsUsable))
                {
                    pendingCount++;
                    record.SurveyStatus = "MANUAL A/B REQUIRED — NON-STOCK AUTO CERT DISABLED";
                    stagedRegistry.ApplyFailure(record,
                        AERISRunwayCertificationState.Pending,
                        AERISRunwayFailureCode.UserCalibrationRequired,
                        "MANUAL A/B CALIBRATION REQUIRED — AUTOMATIC CERTIFICATION IS " +
                        "DISABLED FOR DLC / MOD / USERCFG RUNWAYS");
                    AERISLogger.Info("[AIRFIELD_AUTHORITY_POLICY] manual calibration required; " +
                        "source=" + record.Source + "; site=" +
                        (record.ProviderSiteId ?? string.Empty) + ".");
                    return;
                }

                AERISRunwaySnapshotBuilder.Capture capture;
                AERISRunwayFailureCode failure;
                string detail;
                long nextSequence = ++sequence;
                if (!AERISRunwaySnapshotBuilder.TryBeginCapture(record,
                    record.SurveyDefinition, witness, generation, nextSequence, out capture,
                    out failure, out detail))
                {
                    ApplySnapshotFailure(record, failure, detail);
                    return;
                }
                activeCaptureRecord = record;
                activeCapture = capture;
                activeCaptureSequence = nextSequence;
                activeCaptureStartedTimestamp = Stopwatch.GetTimestamp();
                return;
            }
            ReloadState = AERISAirfieldReloadState.Validating;
            Status = "VALIDATING";
        }

        void ApplySnapshotFailure(AERISProviderFacilityRecord record,
            AERISRunwayFailureCode failure, string detail)
        {
            AERISCachedRunwayRecord cached;
            if (cache.TryGetLastKnownGood(StableRecordId(record), out cached))
            {
                cacheHitCount++;
                MarkCachedDirectionsForRevalidation(cached.Airfield, failure, detail);
                stagedRegistry.ApplyCached(record, cached,
                    "LAST-KNOWN-GOOD CACHE RETAINED FOR DISPLAY — REVALIDATION REQUIRED");
                revalidationCount++;
                return;
            }
            AERISRunwayCertificationState state =
                failure == AERISRunwayFailureCode.ModelUnavailable ||
                failure == AERISRunwayFailureCode.MeshUnreadable ||
                failure == AERISRunwayFailureCode.ColliderUnavailable
                    ? AERISRunwayCertificationState.Pending
                    : AERISRunwayCertificationState.Failed;
            if (state == AERISRunwayCertificationState.Pending) pendingCount++;
            else failedCount++;
            stagedRegistry.ApplyFailure(record, state, failure, detail);
            cache.RecordFailure(record, StableRecordId(record), string.Empty,
                state, failure, detail);
        }

        void SubmitSnapshot(AERISProviderFacilityRecord record,
            AERISRunwaySurveySnapshot snapshot, long snapshotSequence)
        {
            AERISCachedRunwayRecord exact;
            string cacheMissReason;
            if (cache.TryGetExact(snapshot.StableRecordId, snapshot.InputFingerprint,
                out exact, out cacheMissReason))
            {
                cacheHitCount++;
                stagedRegistry.ApplyCached(record, exact, "CERTIFIED CACHE HIT");
                return;
            }
            AERISCachedRunwayRecord compatible;
            string compatibilityReason;
            if (cache.TryGetSubMetreCompatible(snapshot, record, out compatible,
                out compatibilityReason))
            {
                cacheHitCount++;
                AERISLogger.Info("[AIRFIELD_CACHE] sub-metre compatible hit; id=" +
                    SingleLineStableId(snapshot.StableRecordId) + "; " +
                    compatibilityReason + "; source=" +
                    ShortHash(snapshot.SourceFingerprint) + ".");
                stagedRegistry.ApplyCached(record, compatible,
                    "CERTIFIED CACHE HIT — SUB-METRE PROVIDER FRAME COMPATIBILITY");
                return;
            }
            AERISCachedRunwayRecord previous;
            if (cache.TryGetLastKnownGood(snapshot.StableRecordId, out previous))
            {
                AERISLogger.Info("[AIRFIELD_CACHE] exact miss; id=" +
                    SingleLineStableId(snapshot.StableRecordId) + "; reason=" +
                    cacheMissReason + "; cachedPoints=" +
                    previous.GeometryPointCount + "; livePoints=" +
                    snapshot.Points.Length + "; cachedPrimitives=" +
                    previous.GeometryPrimitiveCount + "; livePrimitives=" +
                    snapshot.Primitives.Length + ".");
                revalidationCount++;
                RevokeForRevalidation(record, previous);
            }
            activeSurveyRecord = record;
            activeSurveySnapshot = snapshot;
            activeSurveySequence = snapshotSequence;
            activeSurveyStartedTimestamp = Stopwatch.GetTimestamp();
            if (worker.Submit(new AERISRunwaySurveyJob(generation,
                snapshotSequence, snapshot))) return;
            activeSurveyRecord = null;
            activeSurveySnapshot = null;
            activeSurveyStartedTimestamp = 0L;
            failedCount++;
            stagedRegistry.ApplyFailure(record,
                AERISRunwayCertificationState.Failed,
                AERISRunwayFailureCode.WorkerFailure,
                "CERTIFICATION WORKER REJECTED THE JOB");
            cache.RecordFailure(record, snapshot.StableRecordId,
                snapshot.InputFingerprint, AERISRunwayCertificationState.Failed,
                AERISRunwayFailureCode.WorkerFailure,
                "CERTIFICATION WORKER REJECTED THE JOB");
        }

        static AERISRunwayFailureCode FirstApproachFailure(
            IList<AERISRunwayDefinition> runways)
        {
            if (runways == null) return AERISRunwayFailureCode.None;
            for (int i = 0; i < runways.Count; i++)
                for (int j = 0; j < runways[i].Directions.Count; j++)
                    if (runways[i].Directions[j].FailureCode !=
                        AERISRunwayFailureCode.None)
                        return runways[i].Directions[j].FailureCode;
            return AERISRunwayFailureCode.None;
        }

        static double ElapsedSeconds(long startedTimestamp)
        {
            if (startedTimestamp <= 0L) return 0.0;
            long now = Stopwatch.GetTimestamp();
            if (now <= startedTimestamp || Stopwatch.Frequency <= 0L) return 0.0;
            return (now - startedTimestamp) / (double)Stopwatch.Frequency;
        }

        void TickValidation()
        {
            if (stagedRegistry == null)
            {
                FailReload("NO STAGED DATABASE TO VALIDATE");
                return;
            }
            RetainUndiscoveredCacheRecords();
            stagedRegistry.NormalizeRuntimeProviderPresence(stagedRecords);
            int quarantinedAutomaticDirections =
                stagedRegistry.EnforceCertificationAuthorityPolicy();
            if (quarantinedAutomaticDirections > 0)
                AERISLogger.Warn("[AIRFIELD_AUTHORITY_POLICY] quarantined " +
                    quarantinedAutomaticDirections +
                    " non-stock automatic direction(s); manual A/B is required.");
            stagedRegistry.NormalizeUserCalibratedRunwayPresentation();
            string validationError;
            if (!stagedRegistry.ValidateDatabase(out validationError))
            {
                FailReload("STAGED DATABASE INVALID: " + validationError);
                return;
            }
            stagedRegistry.SortAndCount();
            string cacheError;
            AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache,
                "AIRFIELD_CACHE_SAVE");
            if (!cache.Save(out cacheError))
                AERISLogger.Warn("[AIRFIELD_CACHE] save failed; live database remains valid: " +
                    cacheError);
            RunwaySurveyStatus = "MEASURED " + measuredCount + " / CACHE " +
                cacheHitCount + " / FAILED " + failedCount + " / PENDING " +
                pendingCount + " / REVALIDATE " + revalidationCount +
                " / PROVISIONAL " + provisionalCount + " / SNAPSHOT " + snapshotSliceCount + " SLICE(S), MAX " +
                snapshotMaximumSliceMilliseconds.ToString("0.000",
                    System.Globalization.CultureInfo.InvariantCulture) + " ms, OVERRUN " +
                snapshotHardOverrunCount;
            ReloadState = AERISAirfieldReloadState.Staged;
            Status = approachFrozen ? "STAGED — APPROACH FREEZE ACTIVE" : "STAGED";
        }

        void RetainUndiscoveredCacheRecords()
        {
            if (cache == null || stagedRegistry == null) return;
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (stagedRecords != null)
                for (int i = 0; i < stagedRecords.Count; i++)
                    if (stagedRecords[i] != null)
                    {
                        discovered.Add(StableRecordId(stagedRecords[i]));
                        IList<AERISProviderAlias> aliases =
                            stagedRecords[i].ProviderAliases;
                        if (aliases != null)
                            for (int j = 0; j < aliases.Count; j++)
                                if (aliases[j] != null &&
                                    !string.IsNullOrEmpty(aliases[j].LegacyStableRecordId))
                                    discovered.Add(aliases[j].LegacyStableRecordId);
                    }
            IList<AERISCachedRunwayRecord> records = cache.SnapshotRecords();
            for (int i = 0; i < records.Count; i++)
            {
                AERISCachedRunwayRecord record = records[i];
                if (record == null || record.Airfield == null ||
                    discovered.Contains(record.StableRecordId)) continue;
                stagedRegistry.MergeUndiscoveredCache(record);
                cacheHitCount++;
            }
        }

        void MergeUndiscoveredCache(AERISCachedRunwayRecord cached)
        {
            if (cached == null || cached.Airfield == null ||
                cached.Airfield.CertifiedDirectionCount == 0 ||
                !CachedAuthorityAllowed(cached.Airfield)) return;
            AERISAirfieldDefinition incoming = cached.Airfield.Clone();
            MarkCachedDirectionsForRevalidation(incoming,
                AERISRunwayFailureCode.ModelUnavailable,
                "PROVIDER IS NOT PRESENT IN THE CURRENT DISCOVERY SNAPSHOT");
            incoming.ProviderDetected = false;
            incoming.ProviderRuntimeStatus =
                "CACHE RETAINED FOR DISPLAY — PROVIDER UNAVAILABLE / REVALIDATION";
            AERISAirfieldDefinition existing = null;
            for (int i = 0; i < airfields.Count; i++)
                if (string.Equals(airfields[i].StableId, incoming.StableId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    existing = airfields[i];
                    break;
                }
            if (existing == null)
            {
                airfields.Add(incoming);
                return;
            }
            for (int i = 0; i < incoming.Runways.Count; i++)
            {
                AERISRunwayDefinition runway = incoming.Runways[i];
                bool duplicate = false;
                for (int j = 0; j < existing.Runways.Count; j++)
                    if (string.Equals(existing.Runways[j].StableId, runway.StableId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                if (!duplicate) existing.Runways.Add(runway.Clone());
            }
            existing.Validation = existing.CertifiedDirectionCount > 0
                ? AERISAirfieldValidation.PrecisionValidated
                : AERISAirfieldValidation.DiscoveryOnly;
            if (!existing.ProviderDetected)
                existing.ProviderRuntimeStatus = incoming.ProviderRuntimeStatus;
        }

        void CommitStaged()
        {
            string selectedAirfield = settings == null ? string.Empty :
                settings.LandSelectedAirfieldId;
            string selectedDirection = settings == null ? string.Empty :
                settings.LandSelectedDirectionId;
            var committed = new List<AERISAirfieldDefinition>(stagedRegistry.airfields.Count);
            for (int i = 0; i < stagedRegistry.airfields.Count; i++)
                committed.Add(stagedRegistry.airfields[i].Clone());
            airfields = committed;
            KspProviderStatus = stagedRegistry.KspProviderStatus;
            KerbalKonstructsProviderStatus = stagedRegistry.KerbalKonstructsProviderStatus;
            PhysicalRunwayStatus = stagedRegistry.PhysicalRunwayStatus;
            SortAndCount();
            SelectedAirfieldIndex = -1;
            SelectedDirectionIndex = -1;
            databaseRevision = stagedDatabaseRevision;
            if (mapDramCache != null)
                mapDramCache.PublishAirfields(airfields, databaseRevision,
                    "AIRFIELD_ATOMIC_COMMIT");
            // A new registry/flight must begin with no airport and no runway selected.
            // Only a later in-session reload may restore a selection explicitly made by
            // the user after startup. Never create a default/nearest/first-runway selection.
            if (!startupComplete)
                ResetSelectionForStartup();
            else
                RestoreSelection(selectedAirfield, selectedDirection);
            selectionRevision++;
            revokedDirectionIds.Clear();
            lastLoadUtc = DateTime.UtcNow;
            startupComplete = true;
            ReloadState = AERISAirfieldReloadState.Complete;
            Status = runwayCount == 0 ? "NO RUNWAYS" : "COMPLETE — REV " +
                databaseRevision + " — " + SourceSummary;
            AERISLogger.Info("[AIRFIELD_RELOAD] atomic commit cause=" + refreshCause +
                " generation=" + generation + " databaseRevision=" + databaseRevision +
                "; " + CertificationSummary + "; " + RunwaySurveyStatus + ".");
            stagedRegistry = null;
            stagedRecords = null;
            stagedCatalog = null;
        }

        void FailReload(string reason)
        {
            ReloadState = AERISAirfieldReloadState.Failed;
            Status = "FAILED — " + reason;
            AERISLogger.Warn("[AIRFIELD_RELOAD] " + Status +
                "; previous committed database revision " + databaseRevision + " retained.");
            stagedRegistry = null;
            stagedRecords = null;
            stagedCatalog = null;
            activeSurveyRecord = null;
            activeSurveySnapshot = null;
            activeCaptureRecord = null;
            activeCapture = null;
            activeSurveyStartedTimestamp = 0L;
            activeCaptureStartedTimestamp = 0L;
        }

        bool IsReloadActive()
        {
            return ReloadState == AERISAirfieldReloadState.LoadingCache ||
                ReloadState == AERISAirfieldReloadState.Discovering ||
                ReloadState == AERISAirfieldReloadState.Surveying ||
                ReloadState == AERISAirfieldReloadState.Validating ||
                ReloadState == AERISAirfieldReloadState.Staged;
        }

        void NormalizeRuntimeProviderPresence(IList<AERISProviderFacilityRecord> records)
        {
            // Cached/configured records may carry ProviderDetected=true from an older
            // installation. Clear that stale bit first, then re-authorize only facilities
            // observed by this generation's provider scan.
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (airfield == null) continue;
                if (airfield.Source == AERISAirfieldSource.Stock) continue;
                if (airfield.Source == AERISAirfieldSource.UserCfg) continue;
                airfield.ProviderDetected = false;
            }
            if (records == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                AERISProviderFacilityRecord record = records[i];
                if (record == null) continue;
                AERISAirfieldDefinition airfield = FindConfiguredMatch(record);
                if (airfield == null && UsesProviderAirfieldGroup(record))
                    airfield = FindDiscoveredGroup(record);
                if (airfield == null) continue;
                airfield.ProviderDetected = true;
            }
        }

        static bool AutomaticCertificationAllowed(AERISProviderFacilityRecord record)
        {
            return record != null && record.Source == AERISAirfieldSource.Stock;
        }

        static bool CachedAuthorityAllowed(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return false;
            if (airfield.Source == AERISAirfieldSource.Stock) return true;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction =
                        airfield.Runways[i].Directions[j];
                    if (direction != null &&
                        direction.CertificationState == AERISRunwayCertificationState.Certified &&
                        direction.CertificationBasis == AERISRunwayCertificationBasis.UserCalibrated)
                        return true;
                }
            return false;
        }

        int EnforceCertificationAuthorityPolicy()
        {
            int quarantined = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (airfield == null || airfield.Source == AERISAirfieldSource.Stock) continue;
                bool hasManual = false;
                for (int j = 0; j < airfield.Runways.Count; j++)
                {
                    AERISRunwayDefinition runway = airfield.Runways[j];
                    if (runway == null) continue;
                    for (int k = 0; k < runway.Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction = runway.Directions[k];
                        if (direction == null) continue;
                        if (direction.CertificationBasis ==
                            AERISRunwayCertificationBasis.UserCalibrated &&
                            direction.CertificationState ==
                                AERISRunwayCertificationState.Certified &&
                            direction.HasCertifiedGeometry)
                        {
                            hasManual = true;
                            continue;
                        }
                        direction.CertificationState = AERISRunwayCertificationState.Pending;
                        direction.FailureCode = AERISRunwayFailureCode.UserCalibrationRequired;
                        direction.FailureDetail = string.Empty;
                        direction.PendingDetail =
                            "MANUAL A/B CALIBRATION REQUIRED — NON-STOCK AUTO CERT DISABLED";
                        direction.CertificationBasis = AERISRunwayCertificationBasis.Unknown;
                        direction.CertificationBasisDetail =
                            "NON-STOCK AUTOMATIC GEOMETRY IS DIAGNOSTIC ONLY";
                        direction.ClassificationConfidence = 0.0;
                        direction.GeometryConfidence = 0.0;
                        direction.CertifiedUtc = string.Empty;
                        quarantined++;
                    }
                }
                airfield.Validation = hasManual
                    ? AERISAirfieldValidation.PrecisionValidated
                    : AERISAirfieldValidation.DiscoveryOnly;
                if (!hasManual)
                    airfield.ProviderRuntimeStatus =
                        "MANUAL A/B REQUIRED — NON-STOCK AUTO CERT DISABLED";
            }
            return quarantined;
        }

        bool HasTrustedConfiguredGeometry(AERISProviderFacilityRecord record)
        {
            AERISAirfieldDefinition airfield = FindConfiguredMatch(record);
            // Candidate 5 authority policy: only base-game Stock configured geometry
            // may bypass survey/manual calibration. DLC, KK, SLE and UserCfg geometry
            // are never automatic operational authority.
            return airfield != null && airfield.CertifiedDirectionCount > 0 &&
                airfield.Source == AERISAirfieldSource.Stock;
        }

        void AddConfigured(AERISAirfieldDefinition candidate)
        {
            if (candidate == null) return;
            for (int i = 0; i < airfields.Count; i++)
            {
                if (string.Equals(airfields[i].StableId, candidate.StableId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    AERISLogger.Warn("[AIRFIELD_REGISTRY] duplicate configured airfield ignored: " +
                        candidate.StableId + " from " + candidate.SourcePath);
                    return;
                }
            }
            airfields.Add(candidate);
        }

        void MergeDiscovered(AERISProviderFacilityRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.DisplayName)) return;
            AERISAirfieldDefinition existing = FindConfiguredMatch(record);
            if (existing == null && UsesProviderAirfieldGroup(record))
                existing = FindDiscoveredGroup(record);
            if (existing == null)
            {
                string identity = DiscoveredAirfieldIdentity(record);
                bool grouped = UsesProviderAirfieldGroup(record);
                bool physical = !string.IsNullOrEmpty(record.PhysicalRunwayId);
                existing = new AERISAirfieldDefinition
                {
                    Id = physical ? "DISC_PHYSICAL_" + SanitizeId(record.PhysicalRunwayId) :
                        "DISC_" + record.Source.ToString().ToUpperInvariant() + "_" +
                        SanitizeId(identity),
                    Body = string.IsNullOrEmpty(record.Body) ? "Kerbin" : record.Body,
                    DisplayName = grouped && !string.IsNullOrEmpty(record.ProviderGroup)
                        ? record.ProviderGroup : record.DisplayName,
                    Description = record.Description,
                    Source = record.Source,
                    FacilityKind = record.FacilityKind,
                    Validation = AERISAirfieldValidation.DiscoveryOnly,
                    ProviderSiteId = record.ProviderSiteId,
                    ProviderGroup = record.ProviderGroup,
                    ProviderUuid = record.ProviderUuid,
                    ProviderStableRecordId = AERISProviderIdentity.StableRecordId(record),
                    SourceMod = record.SourceMod,
                    ProviderVersion = record.ProviderVersion,
                    SourcePath = record.SourcePath
                };
                airfields.Add(existing);
            }

            existing.ProviderDetected = true;
            existing.ProviderRuntimeStatus = string.IsNullOrEmpty(record.SurveyStatus)
                ? "DETECTED" : record.SurveyStatus;
            existing.ProviderVersion = record.ProviderVersion;
            if (Math.Abs(record.LatitudeDeg) <= 90.0)
                existing.ReferenceLatitudeDeg = record.LatitudeDeg;
            if (Math.Abs(record.LongitudeDeg) <= 180.0)
                existing.ReferenceLongitudeDeg = record.LongitudeDeg;
            existing.ReferenceElevationMeters = record.ElevationMeters;
            if (string.IsNullOrEmpty(existing.ProviderSiteId))
                existing.ProviderSiteId = record.ProviderSiteId;
            if (string.IsNullOrEmpty(existing.ProviderGroup))
                existing.ProviderGroup = record.ProviderGroup;
            if (string.IsNullOrEmpty(existing.ProviderUuid))
                existing.ProviderUuid = record.ProviderUuid;
            if (string.IsNullOrEmpty(existing.ProviderStableRecordId))
                existing.ProviderStableRecordId = AERISProviderIdentity.StableRecordId(record);
            if (string.IsNullOrEmpty(existing.SourceMod)) existing.SourceMod = record.SourceMod;
            if (existing.Source == AERISAirfieldSource.Unknown ||
                existing.Source == AERISAirfieldSource.UserCfg) existing.Source = record.Source;
            if (record.FacilityKind == AERISFacilityKind.Runway)
                existing.FacilityKind = AERISFacilityKind.Runway;
            else if (existing.FacilityKind == AERISFacilityKind.Unknown)
                existing.FacilityKind = record.FacilityKind;

            if (record.FacilityKind == AERISFacilityKind.Runway &&
                FindRunway(existing, record) == null)
            {
                string runwayIdentity = !string.IsNullOrEmpty(record.PhysicalRunwayId)
                    ? "PHYSICAL_" + SanitizeId(record.PhysicalRunwayId)
                    : "PROVIDER_" + SanitizeId(record.ProviderSiteId);
                var runway = new AERISRunwayDefinition
                {
                    Id = runwayIdentity,
                    DisplayName = record.DisplayName,
                    ProviderSiteId = record.ProviderSiteId,
                    ProviderUuid = record.ProviderUuid,
                    StableId = !string.IsNullOrEmpty(record.PhysicalRunwayId)
                        ? AERISProviderIdentity.StableRecordId(record) + "\n" + runwayIdentity
                        : existing.StableId + "\n" + runwayIdentity,
                    LengthMeters = Math.Max(0.0, record.DeclaredLengthMeters),
                    WidthMeters = Math.Max(0.0, record.DeclaredWidthMeters)
                };
                existing.Runways.Add(runway);
            }
        }

        void ApplyResolved(AERISProviderFacilityRecord record,
            IList<AERISRunwayDefinition> runways, string detail)
        {
            AERISAirfieldDefinition airfield = FindConfiguredMatch(record) ??
                FindDiscoveredGroup(record);
            if (airfield == null) return;
            RemoveProviderRunways(airfield, record);
            if (runways != null)
                for (int i = 0; i < runways.Count; i++) airfield.Runways.Add(runways[i]);
            airfield.Validation = airfield.CertifiedDirectionCount > 0
                ? AERISAirfieldValidation.PrecisionValidated
                : AERISAirfieldValidation.DiscoveryOnly;
            airfield.ProviderDetected = true;
            airfield.ProviderRuntimeStatus = detail ?? string.Empty;
            airfield.ProviderVersion = record.ProviderVersion;
            airfield.ProviderStableRecordId = AERISProviderIdentity.StableRecordId(record);
        }

        void ApplyCached(AERISProviderFacilityRecord record,
            AERISCachedRunwayRecord cached, string status)
        {
            if (cached == null || cached.Airfield == null) return;
            AERISAirfieldDefinition airfield = FindConfiguredMatch(record) ??
                FindDiscoveredGroup(record);
            if (airfield == null)
            {
                airfields.Add(cached.Airfield.Clone());
                return;
            }
            RemoveProviderRunways(airfield, record);
            for (int i = 0; i < cached.Airfield.Runways.Count; i++)
                airfield.Runways.Add(cached.Airfield.Runways[i].Clone());
            airfield.Validation = airfield.CertifiedDirectionCount > 0
                ? AERISAirfieldValidation.PrecisionValidated
                : AERISAirfieldValidation.DiscoveryOnly;
            airfield.ProviderDetected = record.RuntimeBody != null;
            airfield.ProviderRuntimeStatus = status;
            airfield.ProviderVersion = record.ProviderVersion;
            airfield.ProviderStableRecordId = AERISProviderIdentity.StableRecordId(record);
        }

        static void MarkCachedDirectionsForRevalidation(AERISAirfieldDefinition airfield,
            AERISRunwayFailureCode code, string detail)
        {
            if (airfield == null) return;
            for (int i = 0; i < airfield.Runways.Count; i++)
            {
                AERISRunwayDefinition runway = airfield.Runways[i];
                if (runway == null) continue;
                for (int j = 0; j < runway.Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction = runway.Directions[j];
                    if (direction == null) continue;
                    direction.CertificationState =
                        AERISRunwayCertificationState.Revalidation;
                    direction.FailureCode = code;
                    direction.FailureDetail = detail ?? string.Empty;
                    direction.PendingDetail = "REVALIDATION REQUIRED";
                }
            }
            airfield.Validation = AERISAirfieldValidation.DiscoveryOnly;
        }

        void ApplyFailure(AERISProviderFacilityRecord record,
            AERISRunwayCertificationState state, AERISRunwayFailureCode code,
            string detail)
        {
            AERISAirfieldDefinition airfield = FindConfiguredMatch(record) ??
                FindDiscoveredGroup(record);
            if (airfield == null) return;
            AERISRunwayDefinition runway = FindRunway(airfield, record);
            if (runway == null)
            {
                string runwayIdentity = !string.IsNullOrEmpty(record.PhysicalRunwayId)
                    ? "PHYSICAL_" + SanitizeId(record.PhysicalRunwayId)
                    : "PROVIDER_" + SanitizeId(record.ProviderSiteId);
                runway = new AERISRunwayDefinition
                {
                    Id = runwayIdentity,
                    DisplayName = record.DisplayName,
                    ProviderSiteId = record.ProviderSiteId,
                    ProviderUuid = record.ProviderUuid,
                    StableId = !string.IsNullOrEmpty(record.PhysicalRunwayId)
                        ? AERISProviderIdentity.StableRecordId(record) + "\n" + runwayIdentity
                        : airfield.StableId + "\n" + runwayIdentity
                };
                airfield.Runways.Add(runway);
            }
            runway.Directions.Clear();
            double heading = AERISAirfieldConfigParser.NormalizeHeading(
                record.OrientationHeadingDeg);
            for (int i = 0; i < 2; i++)
            {
                double directionHeading = AERISAirfieldConfigParser.NormalizeHeading(
                    heading + i * 180.0);
                runway.Directions.Add(new AERISRunwayDirectionDefinition
                {
                    Id = "UNRESOLVED_" + (i == 0 ? "A" : "B"),
                    DisplayName = "RWY " + RunwayNumber(directionHeading),
                    HeadingDeg = directionHeading,
                    StableId = runway.StableId + "\nUNRESOLVED_" + i,
                    CertificationState = state,
                    FailureCode = code,
                    FailureDetail = state == AERISRunwayCertificationState.Failed ||
                        state == AERISRunwayCertificationState.Revalidation ||
                        state == AERISRunwayCertificationState.Provisional
                            ? detail ?? string.Empty : string.Empty,
                    PendingDetail = state == AERISRunwayCertificationState.Pending ||
                        state == AERISRunwayCertificationState.Provisional
                        ? detail ?? state.ToString().ToUpperInvariant() : string.Empty,
                    CertificationBasis = state == AERISRunwayCertificationState.Provisional
                        ? AERISRunwayCertificationBasis.ProvisionalGeometry
                        : (code == AERISRunwayFailureCode.PlanWitnessConflict
                            ? AERISRunwayCertificationBasis.WitnessConflict
                            : AERISRunwayCertificationBasis.Unknown),
                    CertificationBasisDetail = detail ?? string.Empty,
                    ClassificationConfidence = 0.0,
                    GeometryConfidence = 0.0
                });
            }
            airfield.Validation = AERISAirfieldValidation.DiscoveryOnly;
            airfield.ProviderRuntimeStatus = state.ToString().ToUpperInvariant() + " — " +
                code.ToString().ToUpperInvariant();
        }

        AERISAirfieldDefinition BuildCacheAirfield(AERISProviderFacilityRecord record,
            IList<AERISRunwayDefinition> runways)
        {
            AERISAirfieldDefinition source = FindConfiguredMatch(record) ??
                FindDiscoveredGroup(record);
            var value = new AERISAirfieldDefinition
            {
                Id = source == null ? "CACHE_" + SanitizeId(record.ProviderSiteId) : source.Id,
                Body = record.Body,
                DisplayName = source == null ? record.DisplayName : source.DisplayName,
                Description = source == null ? record.Description : source.Description,
                Source = record.Source,
                FacilityKind = AERISFacilityKind.Runway,
                Validation = AERISAirfieldValidation.PrecisionValidated,
                ProviderSiteId = record.ProviderSiteId,
                ProviderGroup = record.ProviderGroup,
                ProviderUuid = record.ProviderUuid,
                ProviderStableRecordId = AERISProviderIdentity.StableRecordId(record),
                SourceMod = record.SourceMod,
                ProviderVersion = record.ProviderVersion,
                SourcePath = record.SourcePath,
                ProviderDetected = true,
                ProviderRuntimeStatus = "CERTIFIED"
            };
            if (runways != null)
                for (int i = 0; i < runways.Count; i++) value.Runways.Add(runways[i].Clone());
            if (value.Runways.Count > 0 && value.Runways[0].Directions.Count > 0)
            {
                AERISGeoPoint point = value.Runways[0].Directions[0].Threshold;
                value.ReferenceLatitudeDeg = point.LatitudeDeg;
                value.ReferenceLongitudeDeg = point.LongitudeDeg;
                value.ReferenceElevationMeters = point.ElevationMeters;
            }
            return value;
        }

        internal bool VerifyRunwayPlacement(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, AERISRunwayDirectionDefinition direction,
            Vessel vessel, out string detail)
        {
            detail = string.Empty;
            if (airfield == null || runway == null || direction == null)
            {
                detail = "AIRFIELD/RUNWAY/DIRECTION MISSING";
                return false;
            }
            if (vessel == null || vessel.mainBody == null)
            {
                detail = "ACTIVE VESSEL/BODY MISSING";
                return false;
            }
            if (!string.Equals(airfield.Body, vessel.mainBody.bodyName,
                StringComparison.OrdinalIgnoreCase))
            {
                detail = "VESSEL BODY DOES NOT MATCH AIRFIELD";
                return false;
            }
            if ((!vessel.LandedOrSplashed &&
                 vessel.situation != Vessel.Situations.PRELAUNCH) ||
                vessel.situation == Vessel.Situations.SPLASHED)
            {
                detail = "POSITION CHECK INCONCLUSIVE — VESSEL MUST BE PARKED ON THE PHYSICAL RUNWAY";
                return false;
            }
            if (double.IsNaN(vessel.srfSpeed) || double.IsInfinity(vessel.srfSpeed) ||
                vessel.srfSpeed > 5.0)
            {
                detail = "POSITION CHECK INCONCLUSIVE — STOP THE VESSEL BEFORE CHECK HERE";
                return false;
            }
            if (direction.CertificationBasis ==
                AERISRunwayCertificationBasis.UserCalibrated)
            {
                detail = "USER-CALIBRATED RUNWAY PRESERVED — CHECK HERE DOES NOT " +
                    "QUARANTINE OR DELETE A COMPLETE A/B CALIBRATION; USE CLEAR, " +
                    "THEN MARK A/B TO REPLACE IT";
                AERISLogger.Info("[RUNWAY_PLACEMENT_VERIFY] site=" +
                    airfield.ProviderSiteId + "; runway=" + runway.DisplayName +
                    "; result=USER_CALIBRATION_PRESERVED; automaticQuarantine=False.");
                return true;
            }
            if (direction.Threshold == null || direction.OppositeThreshold == null ||
                !direction.Threshold.IsFinite || !direction.OppositeThreshold.IsFinite)
            {
                detail = "RUNWAY ENDPOINT GEOMETRY IS NOT FINITE";
                return false;
            }
            var vesselPoint = new AERISGeoPoint
            {
                LatitudeDeg = vessel.latitude,
                LongitudeDeg = vessel.longitude,
                ElevationMeters = vessel.altitude
            };
            if (!vesselPoint.IsFinite)
            {
                detail = "VESSEL GEO POSITION INVALID";
                return false;
            }
            double bodyRadius = vessel.mainBody.Radius;
            double radius = FiniteNumber(bodyRadius) && bodyRadius > 0.0
                ? bodyRadius : 600000.0;
            double routeLength = AERISAirfieldConfigParser.GreatCircleDistanceMeters(
                direction.Threshold, direction.OppositeThreshold, radius);
            double vesselDistance = AERISAirfieldConfigParser.GreatCircleDistanceMeters(
                direction.Threshold, vesselPoint, radius);
            double routeBearing = InitialBearing(direction.Threshold,
                direction.OppositeThreshold);
            double vesselBearing = InitialBearing(direction.Threshold, vesselPoint);
            if (double.IsNaN(routeLength) || double.IsInfinity(routeLength) ||
                routeLength < 80.0 || double.IsNaN(vesselDistance) ||
                double.IsInfinity(vesselDistance) || double.IsNaN(routeBearing) ||
                double.IsInfinity(routeBearing) || double.IsNaN(vesselBearing) ||
                double.IsInfinity(vesselBearing))
            {
                detail = "RUNWAY PLACEMENT CHECK GEOMETRY IS INVALID";
                return false;
            }
            double delta = NormalizeSignedHeading(vesselBearing - routeBearing) *
                Math.PI / 180.0;
            double along = vesselDistance * Math.Cos(delta);
            double cross = vesselDistance * Math.Sin(delta);
            double rawWidth = runway.WidthMeters;
            double width = FiniteNumber(rawWidth) && rawWidth > 0.0
                ? Math.Max(8.0, rawWidth) : 45.0;
            double centerlineUncertainty = FiniteNumber(
                direction.CenterlineUncertaintyMeters) &&
                direction.CenterlineUncertaintyMeters >= 0.0
                ? direction.CenterlineUncertaintyMeters : 0.0;
            double elevationUncertainty = FiniteNumber(
                direction.ElevationUncertaintyMeters) &&
                direction.ElevationUncertaintyMeters >= 0.0
                ? direction.ElevationUncertaintyMeters : 0.0;
            double corridorGate = Math.Max(width * 0.5 + 12.0,
                centerlineUncertainty * 3.0 + 12.0);
            double endGate = Math.Max(100.0, width * 1.5);
            double elevationGate = Math.Max(25.0,
                elevationUncertainty * 4.0 + 10.0);
            double elevationError = Math.Abs(vessel.altitude -
                direction.Threshold.ElevationMeters);
            bool withinLongitudinalWindow = along >= -endGate &&
                along <= routeLength + endGate;
            if (!withinLongitudinalWindow)
            {
                detail = "POSITION CHECK INCONCLUSIVE — VESSEL IS OUTSIDE RUNWAY END WINDOW; " +
                    "park on the physical runway centerline before CHECK HERE";
                return false;
            }
            if (elevationError > elevationGate)
            {
                detail = "POSITION CHECK INCONCLUSIVE — VESSEL ELEVATION DIFFERS BY " +
                    elevationError.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " M; park on the runway surface before CHECK HERE";
                return false;
            }
            if (Math.Abs(cross) <= corridorGate)
            {
                detail = "PLACEMENT CHECK PASS — CROSS-TRACK " +
                    Math.Abs(cross).ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " M / GATE " + corridorGate.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture) + " M";
                AERISLogger.Info("[RUNWAY_PLACEMENT_VERIFY] site=" +
                    airfield.ProviderSiteId + "; runway=" + runway.DisplayName +
                    "; result=PASS; crossTrack=" + cross.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "m; alongTrack=" + along.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "m; gate=" + corridorGate.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) + "m.");
                return true;
            }
            string observation = "OBSERVED POSITION OUTSIDE CERTIFIED CORRIDOR; runway=" +
                runway.DisplayName + "; cross=" + cross.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "m; along=" + along.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "m; gate=" + corridorGate.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) + "m";
            string stored = string.Empty;
            if (witnessLibrary == null || !witnessLibrary.RecordPlacementMismatch(
                airfield, vessel, cross, along, corridorGate, observation, out stored))
            {
                detail = string.IsNullOrEmpty(stored)
                    ? "PLACEMENT MISMATCH DETECTED BUT QUARANTINE SAVE FAILED" : stored;
                return false;
            }
            detail = "PLACEMENT MISMATCH DETECTED — RUNWAY QUARANTINED; " +
                "MARK A/B AT THE TWO PHYSICAL THRESHOLDS, THEN RESCAN";
            RequestManualReload();
            return false;
        }

        static double InitialBearing(AERISGeoPoint a, AERISGeoPoint b)
        {
            double lat1 = a.LatitudeDeg * Math.PI / 180.0;
            double lat2 = b.LatitudeDeg * Math.PI / 180.0;
            double deltaLon = (b.LongitudeDeg - a.LongitudeDeg) * Math.PI / 180.0;
            while (deltaLon > Math.PI) deltaLon -= Math.PI * 2.0;
            while (deltaLon < -Math.PI) deltaLon += Math.PI * 2.0;
            double y = Math.Sin(deltaLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
            double heading = Math.Atan2(y, x) * 180.0 / Math.PI;
            while (heading < 0.0) heading += 360.0;
            while (heading >= 360.0) heading -= 360.0;
            return heading;
        }

        static double NormalizeSignedHeading(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        static bool FiniteNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal bool MarkUserRunwayCalibration(AERISAirfieldDefinition airfield,
            bool thresholdA, Vessel vessel, out string detail)
        {
            detail = "RUNWAY WITNESS LIBRARY DISABLED";
            if (witnessLibrary == null) return false;
            bool ok = witnessLibrary.MarkCalibration(airfield, thresholdA, vessel, out detail);
            if (ok) RequestManualReload();
            return ok;
        }

        internal bool ClearUserRunwayCalibration(AERISAirfieldDefinition airfield,
            out string detail)
        {
            detail = "RUNWAY WITNESS LIBRARY DISABLED";
            if (witnessLibrary == null) return false;
            bool ok = witnessLibrary.ClearCalibration(airfield, out detail);
            if (ok) RequestManualReload();
            return ok;
        }

        internal string UserRunwayCalibrationSummary(AERISAirfieldDefinition airfield)
        {
            return witnessLibrary == null ? "RUNWAY WITNESS LIBRARY DISABLED"
                : witnessLibrary.CalibrationSummary(airfield);
        }

        internal string UserRunwayCalibrationEndpointSummary(AERISAirfieldDefinition airfield)
        {
            return witnessLibrary == null ? "A/B ABSOLUTE GEO: RUNWAY WITNESS LIBRARY DISABLED"
                : witnessLibrary.CalibrationEndpointSummary(airfield);
        }

        internal bool HasStoredUserCalibration(AERISAirfieldDefinition airfield)
        {
            return witnessLibrary != null && witnessLibrary.HasUsableCalibration(airfield);
        }

        internal bool HasAuthoritativeUserCalibratedPair(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return false;
            int count = 0;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction =
                        airfield.Runways[i].Directions[j];
                    if (direction == null ||
                        direction.CertificationBasis !=
                            AERISRunwayCertificationBasis.UserCalibrated ||
                        EffectiveState(direction) != AERISRunwayCertificationState.Certified ||
                        !direction.HasCertifiedGeometry) continue;
                    count++;
                }
            return count >= 2;
        }

        internal bool IsSupersededByUserCalibration(AERISAirfieldDefinition airfield,
            AERISRunwayDirectionDefinition direction)
        {
            return direction != null &&
                direction.CertificationBasis !=
                    AERISRunwayCertificationBasis.UserCalibrated &&
                HasAuthoritativeUserCalibratedPair(airfield);
        }

        void NormalizeUserCalibratedRunwayPresentation()
        {
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (airfield == null) continue;
                for (int j = 0; j < airfield.Runways.Count; j++)
                {
                    AERISRunwayDefinition runway = airfield.Runways[j];
                    if (runway == null) continue;
                    var runwayNumbers = new List<string>();
                    bool hasManualDirection = false;
                    bool changed = false;
                    for (int k = 0; k < runway.Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction = runway.Directions[k];
                        if (direction == null || direction.CertificationBasis !=
                            AERISRunwayCertificationBasis.UserCalibrated) continue;
                        hasManualDirection = true;
                        string number = RunwayNumber(direction.HeadingDeg);
                        string desiredDirectionName = "RWY " + number;
                        if (!string.Equals(direction.DisplayName, desiredDirectionName,
                            StringComparison.Ordinal))
                        {
                            direction.DisplayName = desiredDirectionName;
                            changed = true;
                        }
                        if (!runwayNumbers.Contains(number)) runwayNumbers.Add(number);
                    }
                    if (!hasManualDirection || runwayNumbers.Count == 0) continue;
                    runwayNumbers.Sort(StringComparer.Ordinal);
                    string desiredRunwayName = "RWY " +
                        string.Join("/", runwayNumbers.ToArray());
                    if (!string.Equals(runway.DisplayName, desiredRunwayName,
                        StringComparison.Ordinal))
                    {
                        runway.DisplayName = desiredRunwayName;
                        changed = true;
                    }
                    if (changed)
                        AERISLogger.Info("[RUNWAY_CALIBRATION] DISPLAY DESIGNATIONS REFRESHED; " +
                            "airfield=" + airfield.DisplayName + "; physicalRunway=" +
                            runway.DisplayName + "; source=GEOMETRY_HEADING; stableIdsPreserved=True.");
                }
            }
        }

        bool ValidateDatabase(out string error)
        {
            error = string.Empty;
            var airfieldIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (airfield == null || string.IsNullOrEmpty(airfield.StableId))
                {
                    error = "NULL/EMPTY AIRFIELD ID";
                    return false;
                }
                if (!airfieldIds.Add(airfield.StableId))
                {
                    error = "DUPLICATE AIRFIELD " + SingleLineStableId(airfield.StableId);
                    return false;
                }
                for (int j = 0; j < airfield.Runways.Count; j++)
                {
                    AERISRunwayDefinition runway = airfield.Runways[j];
                    for (int k = 0; k < runway.Directions.Count; k++)
                    {
                        AERISRunwayDirectionDefinition direction = runway.Directions[k];
                        string stable = string.IsNullOrEmpty(direction.StableId)
                            ? airfield.StableId + "\n" + runway.Id + "\n" + direction.Id
                            : direction.StableId;
                        if (!directionIds.Add(stable))
                        {
                            error = "DUPLICATE APPROACH " + SingleLineStableId(stable);
                            return false;
                        }
                        if (direction.CertificationState ==
                            AERISRunwayCertificationState.Certified &&
                            !direction.HasCertifiedGeometry)
                        {
                            error = "CERTIFIED APPROACH HAS INVALID/LOW-CONFIDENCE GEOMETRY " +
                                SingleLineStableId(stable);
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        AERISAirfieldDefinition FindConfiguredMatch(AERISProviderFacilityRecord record)
        {
            if (record == null) return null;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition value = airfields[i];
                if (!string.Equals(value.Body, record.Body, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(record.ProviderUuid) &&
                    string.Equals(value.ProviderUuid, record.ProviderUuid,
                        StringComparison.OrdinalIgnoreCase)) return value;
                if (!string.IsNullOrEmpty(value.ProviderSiteId) &&
                    string.Equals(value.ProviderSiteId, record.ProviderSiteId,
                        StringComparison.OrdinalIgnoreCase)) return value;
                for (int j = 0; j < value.Runways.Count; j++)
                {
                    if (!string.IsNullOrEmpty(record.ProviderUuid) &&
                        string.Equals(value.Runways[j].ProviderUuid, record.ProviderUuid,
                            StringComparison.OrdinalIgnoreCase)) return value;
                    if (!string.IsNullOrEmpty(value.Runways[j].ProviderSiteId) &&
                        string.Equals(value.Runways[j].ProviderSiteId, record.ProviderSiteId,
                            StringComparison.OrdinalIgnoreCase)) return value;
                }
                IList<AERISProviderAlias> aliases = record.ProviderAliases;
                if (aliases != null)
                    for (int j = 0; j < aliases.Count; j++)
                        if (ProviderAliasMatches(value, aliases[j])) return value;
            }
            return null;
        }

        static bool ProviderAliasMatches(AERISAirfieldDefinition airfield,
            AERISProviderAlias alias)
        {
            if (airfield == null || alias == null) return false;
            if (!string.IsNullOrEmpty(alias.ProviderUuid) &&
                string.Equals(airfield.ProviderUuid, alias.ProviderUuid,
                    StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(alias.ProviderSiteId) &&
                string.Equals(airfield.ProviderSiteId, alias.ProviderSiteId,
                    StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < airfield.Runways.Count; i++)
            {
                AERISRunwayDefinition runway = airfield.Runways[i];
                if (!string.IsNullOrEmpty(alias.ProviderUuid) &&
                    string.Equals(runway.ProviderUuid, alias.ProviderUuid,
                        StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(alias.ProviderSiteId) &&
                    string.Equals(runway.ProviderSiteId, alias.ProviderSiteId,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        AERISAirfieldDefinition FindDiscoveredGroup(AERISProviderFacilityRecord record)
        {
            if (record == null || !UsesProviderAirfieldGroup(record)) return null;
            if (!string.IsNullOrEmpty(record.PhysicalRunwayId))
            {
                string physicalId = "DISC_PHYSICAL_" +
                    SanitizeId(record.PhysicalRunwayId);
                for (int i = 0; i < airfields.Count; i++)
                    if (string.Equals(airfields[i].Body, record.Body,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(airfields[i].Id, physicalId,
                            StringComparison.OrdinalIgnoreCase)) return airfields[i];
                return null;
            }
            string group = string.IsNullOrEmpty(record.ProviderGroup)
                ? record.DisplayName : record.ProviderGroup;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition value = airfields[i];
                if (value.Source != record.Source ||
                    !string.Equals(value.Body, record.Body, StringComparison.OrdinalIgnoreCase)) continue;
                string existingGroup = string.IsNullOrEmpty(value.ProviderGroup)
                    ? value.DisplayName : value.ProviderGroup;
                if (string.Equals(existingGroup, group,
                    StringComparison.OrdinalIgnoreCase)) return value;
            }
            return null;
        }

        static bool UsesProviderAirfieldGroup(AERISProviderFacilityRecord record)
        {
            return record != null &&
                (!string.IsNullOrEmpty(record.PhysicalRunwayId) ||
                 record.Source == AERISAirfieldSource.KerbalKonstructs ||
                 record.Source == AERISAirfieldSource.StockLaunchsitesExpansion);
        }

        static string DiscoveredAirfieldIdentity(AERISProviderFacilityRecord record)
        {
            if (record == null) return "UNNAMED";
            if (!string.IsNullOrEmpty(record.PhysicalRunwayId))
                return record.PhysicalRunwayId;
            if (UsesProviderAirfieldGroup(record) &&
                !string.IsNullOrEmpty(record.ProviderGroup))
                return record.ProviderGroup;
            if (!string.IsNullOrEmpty(record.ProviderSiteId))
                return record.ProviderSiteId;
            if (!string.IsNullOrEmpty(record.ProviderUuid))
                return record.ProviderUuid;
            return string.IsNullOrEmpty(record.DisplayName)
                ? "UNNAMED" : record.DisplayName;
        }

        // The provider signature is an identity-set signature.  It deliberately
        // excludes runtime geometry because KK/Unity can rebuild equivalent
        // facilities with slightly different transform-derived doubles on each
        // process launch.  Geometry remains visible through a separate diagnostic
        // signature and through the certification fingerprint/cache gates.
        static string ProviderSignatureIdentity(AERISProviderFacilityRecord record)
        {
            if (record == null) return string.Empty;
            bool hasStableProviderFields = !string.IsNullOrEmpty(record.ProviderSiteId) ||
                !string.IsNullOrEmpty(record.SourcePath) ||
                !string.IsNullOrEmpty(record.ModelName);
            string uuidFallback = hasStableProviderFields ? string.Empty :
                record.ProviderUuid ?? string.Empty;
            if (!string.IsNullOrEmpty(record.PhysicalRunwayId))
                return (record.Body ?? string.Empty) + "|PHYSICAL|" +
                    record.PhysicalRunwayId + "|" + record.FacilityKind.ToString();
            return (record.Body ?? string.Empty) + "|" +
                record.Source.ToString() + "|" + record.FacilityKind.ToString() + "|" +
                (record.ProviderSiteId ?? string.Empty) + "|" +
                (record.ProviderGroup ?? string.Empty) + "|" +
                (record.ModelName ?? string.Empty) + "|" +
                (record.SourcePath ?? string.Empty) + "|" + uuidFallback;
        }

        static string ProviderGeometryDiagnosticIdentity(
            AERISProviderFacilityRecord record)
        {
            if (record == null) return string.Empty;
            return ProviderSignatureIdentity(record) + "|" +
                QuantizedSignatureNumber(record.LatitudeDeg, 0.000001) + "|" +
                QuantizedSignatureNumber(record.LongitudeDeg, 0.000001) + "|" +
                QuantizedSignatureNumber(record.ElevationMeters, 0.1) + "|" +
                QuantizedSignatureNumber(record.OrientationHeadingDeg, 0.01) + "|" +
                QuantizedSignatureNumber(record.DeclaredLengthMeters, 0.1) + "|" +
                QuantizedSignatureNumber(record.DeclaredWidthMeters, 0.1) + "|" +
                QuantizedSignatureNumber(record.RuntimeModelScale, 0.001);
        }

        static string QuantizedSignatureNumber(double value, double quantum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || quantum <= 0.0)
                return "NA";
            double scaled = Math.Round(value / quantum, MidpointRounding.AwayFromZero);
            return scaled.ToString("0",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        static string HashProviderIdentities(List<string> identities)
        {
            identities.Sort(StringComparer.OrdinalIgnoreCase);
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < identities.Count; i++)
            {
                string value = identities[i] ?? string.Empty;
                for (int j = 0; j < value.Length; j++)
                {
                    hash ^= (ulong)char.ToUpperInvariant(value[j]);
                    hash *= prime;
                }
                hash ^= 0x0A;
                hash *= prime;
            }
            return hash.ToString("X16",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        static string ProviderSnapshotSignature(
            IList<AERISProviderFacilityRecord> records, out int runwayRecords,
            out string geometryDiagnosticSignature)
        {
            runwayRecords = 0;
            var identities = new List<string>();
            var geometryIdentities = new List<string>();
            if (records != null)
                for (int i = 0; i < records.Count; i++)
                {
                    AERISProviderFacilityRecord record = records[i];
                    if (record == null) continue;
                    if (record.FacilityKind == AERISFacilityKind.Runway) runwayRecords++;
                    identities.Add(ProviderSignatureIdentity(record));
                    geometryIdentities.Add(ProviderGeometryDiagnosticIdentity(record));
                }
            geometryDiagnosticSignature = HashProviderIdentities(geometryIdentities);
            return HashProviderIdentities(identities);
        }

        static string ShortHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return "EMPTY";
            return value.Length <= 12 ? value : value.Substring(0, 12);
        }

        static string SingleLineStableId(string value)
        {
            return (value ?? string.Empty).Replace("\r", "\\r").Replace("\n", " / ");
        }

        static AERISRunwayDefinition FindRunway(AERISAirfieldDefinition airfield,
            AERISProviderFacilityRecord record)
        {
            if (airfield == null || record == null) return null;
            string physicalStable = string.IsNullOrEmpty(record.PhysicalRunwayId)
                ? string.Empty : AERISProviderIdentity.StableRecordId(record) + "\n";
            for (int i = 0; i < airfield.Runways.Count; i++)
            {
                AERISRunwayDefinition runway = airfield.Runways[i];
                if (!string.IsNullOrEmpty(physicalStable) &&
                    (runway.StableId ?? string.Empty).StartsWith(physicalStable,
                        StringComparison.OrdinalIgnoreCase)) return runway;
                if (!string.IsNullOrEmpty(record.ProviderUuid) &&
                    string.Equals(runway.ProviderUuid, record.ProviderUuid,
                        StringComparison.OrdinalIgnoreCase)) return runway;
                if (!string.IsNullOrEmpty(runway.ProviderSiteId) &&
                    string.Equals(runway.ProviderSiteId, record.ProviderSiteId,
                        StringComparison.OrdinalIgnoreCase)) return runway;
            }
            return null;
        }

        static void RemoveProviderRunways(AERISAirfieldDefinition airfield,
            AERISProviderFacilityRecord record)
        {
            if (airfield == null || record == null) return;
            string physicalStable = string.IsNullOrEmpty(record.PhysicalRunwayId)
                ? string.Empty : AERISProviderIdentity.StableRecordId(record) + "\n";
            for (int i = airfield.Runways.Count - 1; i >= 0; i--)
            {
                AERISRunwayDefinition runway = airfield.Runways[i];
                bool samePhysical = !string.IsNullOrEmpty(physicalStable) &&
                    (runway.StableId ?? string.Empty).StartsWith(physicalStable,
                        StringComparison.OrdinalIgnoreCase);
                bool sameUuid = !string.IsNullOrEmpty(record.ProviderUuid) &&
                    string.Equals(runway.ProviderUuid, record.ProviderUuid,
                        StringComparison.OrdinalIgnoreCase);
                bool sameSite = !string.IsNullOrEmpty(record.ProviderSiteId) &&
                    string.Equals(runway.ProviderSiteId, record.ProviderSiteId,
                        StringComparison.OrdinalIgnoreCase);
                if (samePhysical || sameUuid || sameSite) airfield.Runways.RemoveAt(i);
            }
        }

        void SortAndCount()
        {
            airfields.Sort(delegate(AERISAirfieldDefinition a, AERISAirfieldDefinition b)
            {
                int body = string.Compare(a.Body, b.Body, StringComparison.OrdinalIgnoreCase);
                if (body != 0) return body;
                int source = a.Source.CompareTo(b.Source);
                return source != 0 ? source : string.Compare(a.DisplayName, b.DisplayName,
                    StringComparison.OrdinalIgnoreCase);
            });
            stockCount = dlcDefinedCount = dlcDetectedCount = kkCount = sleCount =
                validatedCount = runwayCount = registeredApproachCount = 0;
            for (int i = 0; i < airfields.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfields[i];
                if (airfield.FacilityKind != AERISFacilityKind.Runway) continue;
                runwayCount += airfield.Runways.Count;
                registeredApproachCount += airfield.DirectionCount;
                switch (airfield.Source)
                {
                    case AERISAirfieldSource.Stock: stockCount++; break;
                    case AERISAirfieldSource.Dlc:
                        dlcDefinedCount++;
                        if (airfield.ProviderDetected) dlcDetectedCount++;
                        break;
                    case AERISAirfieldSource.KerbalKonstructs: kkCount++; break;
                    case AERISAirfieldSource.StockLaunchsitesExpansion: sleCount++; break;
                }
                if (airfield.CanArmFoundation) validatedCount++;
            }
        }

        void ResetSelectionForStartup()
        {
            SelectedAirfieldIndex = -1;
            SelectedDirectionIndex = -1;
            if (settings != null)
            {
                settings.LandSelectedAirfieldId = string.Empty;
                settings.LandSelectedDirectionId = string.Empty;
                settings.LandSelectionExplicitlyCleared = true;
                settings.Save();
            }
            AERISLogger.Info("[AIRFIELD_SELECTION] startup neutral; airport=NONE; runway=NONE.");
        }

        void RestoreSelection(string airfieldId, string directionId)
        {
            SelectedAirfieldIndex = -1;
            SelectedDirectionIndex = -1;
            if (settings != null && settings.LandSelectionExplicitlyCleared) return;
            if (string.IsNullOrEmpty(airfieldId)) return;

            IList<AERISAirfieldDefinition> values = Airfields;
            for (int i = 0; i < values.Count; i++)
            {
                if (SelectableDirectionCount(values[i]) == 0) continue;
                if (string.Equals(values[i].StableId, airfieldId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    SelectedAirfieldIndex = i;
                    break;
                }
            }
            AERISAirfieldDefinition selected = SelectedAirfield;
            if (selected == null || SelectableDirectionCount(selected) == 0)
            {
                SelectedAirfieldIndex = -1;
                return;
            }
            if (string.IsNullOrEmpty(directionId))
            {
                SelectedAirfieldIndex = -1;
                return;
            }
            for (int i = 0; i < SelectableDirectionCount(selected); i++)
            {
                AERISRunwayDirectionDefinition direction = SelectableDirectionAt(selected, i);
                if (direction != null && string.Equals(direction.StableId, directionId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    SelectedDirectionIndex = i;
                    return;
                }
            }
            SelectedAirfieldIndex = -1;
            SelectedDirectionIndex = -1;
        }

        void PersistSelection()
        {
            if (settings == null) return;
            settings.LandSelectedAirfieldId = SelectedAirfield == null
                ? string.Empty : SelectedAirfield.StableId;
            settings.LandSelectedDirectionId = SelectedDirection == null
                ? string.Empty : SelectedDirection.StableId;
            settings.Save();
        }

        bool HasState(AERISAirfieldDefinition airfield,
            AERISRunwayCertificationState state)
        {
            if (airfield == null) return false;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction =
                        airfield.Runways[i].Directions[j];
                    if (IsSupersededByUserCalibration(airfield, direction)) continue;
                    if (EffectiveState(direction) == state) return true;
                }
            return false;
        }

        internal AERISRunwayCertificationState EffectiveState(
            AERISRunwayDirectionDefinition direction)
        {
            if (direction == null) return AERISRunwayCertificationState.Pending;
            return revokedDirectionIds.Contains(direction.StableId)
                ? AERISRunwayCertificationState.Revalidation
                : direction.CertificationState;
        }

        internal bool IsDirectionRevoked(string stableId)
        {
            return !string.IsNullOrEmpty(stableId) && revokedDirectionIds.Contains(stableId);
        }

        internal int SelectableDirectionCount(AERISAirfieldDefinition airfield)
        {
            if (airfield == null || !IsAirfieldPresentationAvailable(airfield) ||
                airfield.FacilityKind != AERISFacilityKind.Runway) return 0;
            bool manualAuthoritative = HasAuthoritativeUserCalibratedPair(airfield);
            int count = 0;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction =
                        airfield.Runways[i].Directions[j];
                    if (manualAuthoritative && direction.CertificationBasis !=
                        AERISRunwayCertificationBasis.UserCalibrated) continue;
                    if (direction.HasCertifiedGeometry &&
                        !revokedDirectionIds.Contains(direction.StableId)) count++;
                }
            return count;
        }

        internal AERISRunwayDirectionDefinition SelectableDirectionAt(
            AERISAirfieldDefinition airfield, int index)
        {
            if (airfield == null || !IsAirfieldPresentationAvailable(airfield) ||
                index < 0) return null;
            bool manualAuthoritative = HasAuthoritativeUserCalibratedPair(airfield);
            int cursor = 0;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction =
                        airfield.Runways[i].Directions[j];
                    if (manualAuthoritative && direction.CertificationBasis !=
                        AERISRunwayCertificationBasis.UserCalibrated) continue;
                    if (!direction.HasCertifiedGeometry ||
                        revokedDirectionIds.Contains(direction.StableId)) continue;
                    if (cursor == index) return direction;
                    cursor++;
                }
            return null;
        }

        void RevokeForRevalidation(AERISProviderFacilityRecord record,
            AERISCachedRunwayRecord previous)
        {
            if (previous != null && previous.Airfield != null)
                for (int i = 0; i < previous.Airfield.Runways.Count; i++)
                    for (int j = 0; j < previous.Airfield.Runways[i].Directions.Count; j++)
                    {
                        string stable = previous.Airfield.Runways[i].Directions[j].StableId;
                        if (!string.IsNullOrEmpty(stable)) revokedDirectionIds.Add(stable);
                    }
            // Also revoke matching committed geometry in case its stable ID originated
            // before the cache schema was introduced.
            for (int i = 0; i < airfields.Count; i++)
                for (int j = 0; j < airfields[i].Runways.Count; j++)
                {
                    AERISRunwayDefinition runway = airfields[i].Runways[j];
                    bool match = record != null &&
                        ((!string.IsNullOrEmpty(record.ProviderUuid) &&
                          string.Equals(runway.ProviderUuid, record.ProviderUuid,
                              StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrEmpty(record.ProviderSiteId) &&
                          string.Equals(runway.ProviderSiteId, record.ProviderSiteId,
                              StringComparison.OrdinalIgnoreCase)));
                    if (!match) continue;
                    for (int k = 0; k < runway.Directions.Count; k++)
                        if (!string.IsNullOrEmpty(runway.Directions[k].StableId))
                            revokedDirectionIds.Add(runway.Directions[k].StableId);
                }
        }

        static string StableRecordId(AERISProviderFacilityRecord record)
        {
            return AERISProviderIdentity.StableRecordId(record);
        }

        static string RunwayNumber(double heading)
        {
            int number = (int)Math.Floor((AERISAirfieldConfigParser.NormalizeHeading(heading) +
                5.0) / 10.0) % 36;
            if (number <= 0) number = 36;
            return number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string ResolvePath(string relative)
        {
            return Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath,
                relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        static string SanitizeId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "UNNAMED";
            char[] chars = value.ToUpperInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            string result = new string(chars);
            while (result.Contains("__")) result = result.Replace("__", "_");
            return result.Trim('_');
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            generation++;
            if (worker != null) worker.Dispose();
            stagedRegistry = null;
            stagedRecords = null;
            activeSurveyRecord = null;
            activeSurveySnapshot = null;
            activeCaptureRecord = null;
            activeCapture = null;
            activeSurveyStartedTimestamp = 0L;
            activeCaptureStartedTimestamp = 0L;
        }
    }
}
