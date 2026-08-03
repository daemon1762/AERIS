using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISCachedRunwayRecord
    {
        internal string StableRecordId = string.Empty;
        internal string Fingerprint = string.Empty;
        internal string SourceFingerprint = string.Empty;
        internal int AlgorithmVersion;
        internal string ProviderVersion = string.Empty;
        internal string Body = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderSiteId = string.Empty;
        internal string SourcePath = string.Empty;
        internal string ModelName = string.Empty;
        internal string SourceMod = string.Empty;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double ElevationMeters;
        internal double HeadingDeg;
        internal double ModelScale = 1.0;
        internal int GeometryPointCount;
        internal int GeometryPrimitiveCount;
        internal bool ColliderReadable;
        internal string GameBuild = string.Empty;
        internal string AerisBuild = string.Empty;
        internal string SavedUtc = string.Empty;
        internal AERISAirfieldDefinition Airfield;
    }

    internal sealed class AERISCachedRunwayFailure
    {
        internal string StableRecordId = string.Empty;
        internal string Fingerprint = string.Empty;
        internal int AlgorithmVersion;
        internal AERISRunwayCertificationState State = AERISRunwayCertificationState.Failed;
        internal AERISRunwayFailureCode Code = AERISRunwayFailureCode.None;
        internal string Detail = string.Empty;
        internal string ProviderVersion = string.Empty;
        internal string Body = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderSiteId = string.Empty;
        internal string SourcePath = string.Empty;
        internal string ModelName = string.Empty;
        internal string SavedUtc = string.Empty;
    }

    internal sealed class AERISRunwayCertificationCache
    {
        const int CurrentSchemaVersion = 8;
        const int PreviousSchemaVersion = 7;
        const int CompatibilitySchemaVersion = 6;
        const int OlderCompatibilitySchemaVersion = 5;
        const int LegacySchemaVersion = 4;
        const int OldestLegacySchemaVersion = 3;
        const string CacheNodeName = "AERISAirfieldCertificationCache";
        const string StableIdEncoding = "base64-utf8-v1";
        const string RelativePath =
            "GameData/AERISFlightControl/PluginData/AirfieldCertificationCache.cfg";
        readonly Dictionary<string, AERISCachedRunwayRecord> entries =
            new Dictionary<string, AERISCachedRunwayRecord>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AERISCachedRunwayFailure> failures =
            new Dictionary<string, AERISCachedRunwayFailure>(StringComparer.OrdinalIgnoreCase);

        internal int Count { get { return entries.Count; } }
        internal int FailureCount { get { return failures.Count; } }
        internal string LastStatus { get; private set; } = "NOT LOADED";

        internal int PurgeNonStockAutomaticAuthority()
        {
            var remove = new List<string>();
            foreach (KeyValuePair<string, AERISCachedRunwayRecord> item in entries)
            {
                AERISCachedRunwayRecord record = item.Value;
                if (record == null || record.Airfield == null)
                {
                    remove.Add(item.Key);
                    continue;
                }
                if (record.Airfield.Source == AERISAirfieldSource.Stock) continue;
                bool manualAuthority = false;
                for (int i = 0; i < record.Airfield.Runways.Count && !manualAuthority; i++)
                    for (int j = 0; j < record.Airfield.Runways[i].Directions.Count; j++)
                    {
                        AERISRunwayDirectionDefinition direction =
                            record.Airfield.Runways[i].Directions[j];
                        if (direction != null &&
                            direction.CertificationState ==
                                AERISRunwayCertificationState.Certified &&
                            direction.CertificationBasis ==
                                AERISRunwayCertificationBasis.UserCalibrated)
                        {
                            manualAuthority = true;
                            break;
                        }
                    }
                if (!manualAuthority) remove.Add(item.Key);
            }
            for (int i = 0; i < remove.Count; i++)
            {
                entries.Remove(remove[i]);
                failures.Remove(remove[i]);
            }
            if (remove.Count > 0)
                LastStatus += " / POLICY PURGED " + remove.Count +
                    " NON-STOCK AUTOMATIC RECORD(S)";
            return remove.Count;
        }

        internal void Load()
        {
            string path = ResolvePath();
            if (!File.Exists(path))
            {
                LastStatus = entries.Count == 0 ? "CACHE EMPTY" :
                    "CACHE FILE MISSING — " + entries.Count + " MEMORY RECORD(S) RETAINED";
                return;
            }
            var loadedEntries = new Dictionary<string, AERISCachedRunwayRecord>(
                StringComparer.OrdinalIgnoreCase);
            var loadedFailures = new Dictionary<string, AERISCachedRunwayFailure>(
                StringComparer.OrdinalIgnoreCase);
            string error;
            if (!TryLoadFile(path, loadedEntries, loadedFailures, out error))
            {
                string backup = path + ".bak";
                loadedEntries.Clear();
                loadedFailures.Clear();
                string backupError;
                if (File.Exists(backup) && TryLoadFile(backup, loadedEntries,
                    loadedFailures, out backupError))
                {
                    entries.Clear();
                    failures.Clear();
                    foreach (KeyValuePair<string, AERISCachedRunwayRecord> item in loadedEntries)
                        entries[item.Key] = item.Value;
                    foreach (KeyValuePair<string, AERISCachedRunwayFailure> item in loadedFailures)
                        failures[item.Key] = item.Value;
                    LastStatus = "CACHE RECOVERED FROM BACKUP — " + entries.Count +
                        " CERTIFIED / " + failures.Count + " FAILURE RECORD(S)";
                    AERISLogger.Warn("[AIRFIELD_CACHE] primary cache rejected (" + error +
                        "); recovered the last valid backup.");
                    return;
                }
                LastStatus = "CACHE LOAD FAILED — " + entries.Count +
                    " MEMORY RECORD(S) RETAINED";
                AERISLogger.Warn("[AIRFIELD_CACHE] " + LastStatus + " — " + error);
                return;
            }
            entries.Clear();
            failures.Clear();
            foreach (KeyValuePair<string, AERISCachedRunwayRecord> item in loadedEntries)
                entries[item.Key] = item.Value;
            foreach (KeyValuePair<string, AERISCachedRunwayFailure> item in loadedFailures)
                failures[item.Key] = item.Value;
            LastStatus = "CACHE " + entries.Count + " CERTIFIED / " + failures.Count +
                " FAILURE RECORD(S)";
            AERISLogger.Info("[AIRFIELD_CACHE] load accepted; certified=" +
                entries.Count + "; failures=" + failures.Count + ".");
        }

        bool TryLoadFile(string path,
            IDictionary<string, AERISCachedRunwayRecord> destination,
            IDictionary<string, AERISCachedRunwayFailure> failureDestination,
            out string error)
        {
            error = string.Empty;
            try
            {
                ConfigNode loaded = ConfigNode.Load(path);
                if (loaded == null)
                {
                    error = "CACHE UNREADABLE";
                    return false;
                }
                ConfigNode root = ResolveCacheRoot(loaded);
                if (root == null)
                {
                    error = "CACHE ROOT MISSING";
                    return false;
                }
                int schemaVersion = ReadInt(root, "schemaVersion");
                if (schemaVersion != CurrentSchemaVersion &&
                    schemaVersion != PreviousSchemaVersion &&
                    schemaVersion != CompatibilitySchemaVersion &&
                    schemaVersion != OlderCompatibilitySchemaVersion &&
                    schemaVersion != LegacySchemaVersion &&
                    schemaVersion != OldestLegacySchemaVersion)
                {
                    error = "CACHE SCHEMA " + schemaVersion + " IS NOT SUPPORTED";
                    return false;
                }
                ConfigNode[] records = root.GetNodes("Record");
                int certifiedAliasesCompacted = 0;
                for (int i = 0; i < records.Length; i++)
                {
                    AERISCachedRunwayRecord record = ParseRecord(records[i], schemaVersion);
                    if (record == null || string.IsNullOrEmpty(record.StableRecordId) ||
                        record.Airfield == null) continue;
                    AERISCachedRunwayRecord existing;
                    if (destination.TryGetValue(record.StableRecordId, out existing))
                    {
                        certifiedAliasesCompacted++;
                        if (PreferCandidate(existing, record))
                            destination[record.StableRecordId] = record;
                    }
                    else destination[record.StableRecordId] = record;
                }
                ConfigNode[] failureNodes = root.GetNodes("FailureRecord");
                if (schemaVersion == OldestLegacySchemaVersion)
                {
                    // Schema 3 wrote newline-delimited identities through ConfigNode's
                    // single-line value format.  The fields needed to reverse that flattening
                    // were not stored on failure records.  These records are only negative
                    // acceleration hints, never safety authority, so discard and rebuild them
                    // rather than carrying ambiguous aliases into later schemas.
                    if (failureNodes.Length > 0)
                        AERISLogger.Info("[AIRFIELD_CACHE] schema-3 migration retained " +
                            destination.Count + " certified record(s) and discarded " +
                            failureNodes.Length + " ambiguous failure hint(s) for safe rebuild.");
                }
                else
                {
                    int failureAliasesCompacted = 0;
                    for (int i = 0; i < failureNodes.Length; i++)
                    {
                        AERISCachedRunwayFailure failure = ParseFailure(failureNodes[i], schemaVersion);
                        if (failure == null || string.IsNullOrEmpty(failure.StableRecordId)) continue;
                        AERISCachedRunwayFailure existing;
                        if (failureDestination.TryGetValue(failure.StableRecordId, out existing))
                        {
                            failureAliasesCompacted++;
                            if (PreferCandidate(existing, failure))
                                failureDestination[failure.StableRecordId] = failure;
                        }
                        else failureDestination[failure.StableRecordId] = failure;
                    }
                    if (certifiedAliasesCompacted > 0 || failureAliasesCompacted > 0)
                        AERISLogger.Info("[AIRFIELD_CACHE] canonical identity migration compacted " +
                            certifiedAliasesCompacted + " certified alias(es) and " +
                            failureAliasesCompacted + " failure alias(es).");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        internal IList<AERISCachedRunwayRecord> SnapshotRecords()
        {
            var keys = new List<string>(entries.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var result = new List<AERISCachedRunwayRecord>(keys.Count);
            for (int i = 0; i < keys.Count; i++) result.Add(Clone(entries[keys[i]]));
            return result.AsReadOnly();
        }

        // v0.18 Gate 0: migrate provider-specific cache aliases to the single
        // physical-runway authority chosen by discovery.  This runs only after the
        // full provider snapshot has been captured and conservatively federated.
        internal void CompactPhysicalAliases(
            IList<AERISProviderFacilityRecord> providers,
            out int certifiedCompacted, out int failureCompacted)
        {
            certifiedCompacted = 0;
            failureCompacted = 0;
            if (providers == null || providers.Count == 0) return;

            for (int i = 0; i < providers.Count; i++)
            {
                AERISProviderFacilityRecord provider = providers[i];
                if (provider == null ||
                    provider.FacilityKind != AERISFacilityKind.Runway ||
                    string.IsNullOrEmpty(provider.PhysicalRunwayId)) continue;

                string canonicalKey = AERISProviderIdentity.StableRecordId(provider);
                if (string.IsNullOrEmpty(canonicalKey)) continue;
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                aliases.Add(canonicalKey);
                IList<AERISProviderAlias> providerAliases = provider.ProviderAliases;
                if (providerAliases != null)
                    for (int j = 0; j < providerAliases.Count; j++)
                    {
                        AERISProviderAlias alias = providerAliases[j];
                        if (alias != null &&
                            !string.IsNullOrEmpty(alias.LegacyStableRecordId))
                            aliases.Add(alias.LegacyStableRecordId);
                    }

                AERISCachedRunwayRecord bestRecord = null;
                AERISCachedRunwayFailure bestFailure = null;
                foreach (string key in aliases)
                {
                    AERISCachedRunwayRecord record;
                    if (entries.TryGetValue(key, out record))
                    {
                        if (PreferCandidate(bestRecord, record)) bestRecord = Clone(record);
                    }
                    AERISCachedRunwayFailure failure;
                    if (failures.TryGetValue(key, out failure))
                    {
                        if (PreferCandidate(bestFailure, failure))
                            bestFailure = Clone(failure);
                    }
                }

                bool hadCanonicalFailure = failures.ContainsKey(canonicalKey);
                foreach (string key in aliases)
                {
                    bool wasCertifiedAlias = !string.Equals(key, canonicalKey,
                        StringComparison.OrdinalIgnoreCase) && entries.ContainsKey(key);
                    bool wasFailureAlias = !string.Equals(key, canonicalKey,
                        StringComparison.OrdinalIgnoreCase) && failures.ContainsKey(key);
                    entries.Remove(key);
                    failures.Remove(key);
                    if (wasCertifiedAlias) certifiedCompacted++;
                    if (wasFailureAlias) failureCompacted++;
                }

                if (bestRecord != null)
                {
                    RebindPhysicalIdentity(bestRecord, canonicalKey, provider);
                    entries[canonicalKey] = bestRecord;
                    // A positive certification always supersedes stale negative hints
                    // for every provider alias in the same physical runway cluster.
                    if (hadCanonicalFailure) failureCompacted++;
                }
                else if (bestFailure != null)
                {
                    RebindPhysicalIdentity(bestFailure, canonicalKey, provider);
                    failures[canonicalKey] = bestFailure;
                }

            }

            if (certifiedCompacted > 0 || failureCompacted > 0)
                AERISLogger.Info("[AIRFIELD_CACHE] physical runway alias migration " +
                    "compacted " + certifiedCompacted + " certified and " +
                    failureCompacted + " failure alias(es).");
        }

        static void RebindPhysicalIdentity(AERISCachedRunwayRecord record,
            string canonicalKey, AERISProviderFacilityRecord provider)
        {
            if (record == null || provider == null) return;
            record.StableRecordId = canonicalKey;
            record.Body = provider.Body ?? string.Empty;
            record.ProviderUuid = provider.ProviderUuid ?? string.Empty;
            record.ProviderSiteId = provider.ProviderSiteId ?? string.Empty;
            record.SourcePath = provider.SourcePath ?? string.Empty;
            record.ModelName = provider.ModelName ?? string.Empty;
            record.SourceMod = provider.SourceMod ?? string.Empty;
            record.ProviderVersion = provider.ProviderVersion ?? string.Empty;
            if (record.Airfield != null)
            {
                record.Airfield.ProviderUuid = record.ProviderUuid;
                record.Airfield.ProviderSiteId = record.ProviderSiteId;
                record.Airfield.ProviderGroup = provider.ProviderGroup ?? string.Empty;
                record.Airfield.SourcePath = record.SourcePath;
                record.Airfield.SourceMod = record.SourceMod;
                record.Airfield.ProviderVersion = record.ProviderVersion;
                NormalizeCachedStableIds(canonicalKey, record.Airfield);
            }
        }

        static void RebindPhysicalIdentity(AERISCachedRunwayFailure failure,
            string canonicalKey, AERISProviderFacilityRecord provider)
        {
            if (failure == null || provider == null) return;
            failure.StableRecordId = canonicalKey;
            failure.Body = provider.Body ?? string.Empty;
            failure.ProviderUuid = provider.ProviderUuid ?? string.Empty;
            failure.ProviderSiteId = provider.ProviderSiteId ?? string.Empty;
            failure.SourcePath = provider.SourcePath ?? string.Empty;
            failure.ModelName = provider.ModelName ?? string.Empty;
            failure.ProviderVersion = provider.ProviderVersion ?? string.Empty;
        }

        internal bool TryGetExact(string stableRecordId, string fingerprint,
            out AERISCachedRunwayRecord record)
        {
            string reason;
            return TryGetExact(stableRecordId, fingerprint, out record, out reason);
        }

        internal bool TryGetExact(string stableRecordId, string fingerprint,
            out AERISCachedRunwayRecord record, out string reason)
        {
            record = null;
            reason = string.Empty;
            if (string.IsNullOrEmpty(stableRecordId))
            {
                reason = "STABLE_ID_EMPTY";
                return false;
            }
            AERISCachedRunwayRecord value;
            if (!entries.TryGetValue(stableRecordId, out value))
            {
                reason = "NO_RECORD";
                return false;
            }
            if (value.AlgorithmVersion != AERISRunwaySurveySnapshot.CurrentAlgorithmVersion)
            {
                reason = "ALGORITHM " + value.AlgorithmVersion + " -> " +
                    AERISRunwaySurveySnapshot.CurrentAlgorithmVersion;
                return false;
            }
            if (!string.Equals(value.Fingerprint, fingerprint,
                StringComparison.OrdinalIgnoreCase))
            {
                reason = "FINGERPRINT " + ShortHash(value.Fingerprint) + " -> " +
                    ShortHash(fingerprint);
                return false;
            }
            record = Clone(value);
            reason = "EXACT";
            return true;
        }

        internal bool TryGetSubMetreCompatible(AERISRunwaySurveySnapshot snapshot,
            AERISProviderFacilityRecord provider, out AERISCachedRunwayRecord record,
            out string reason)
        {
            const double MaximumHorizontalMeters = 0.50;
            const double MaximumVerticalMeters = 0.10;
            const double MaximumHeadingDegrees = 0.02;
            const double MaximumScaleDelta = 0.0005;
            record = null;
            reason = string.Empty;
            if (snapshot == null || string.IsNullOrEmpty(snapshot.StableRecordId))
            {
                reason = "SNAPSHOT_EMPTY";
                return false;
            }
            AERISCachedRunwayRecord value;
            if (!entries.TryGetValue(snapshot.StableRecordId, out value))
            {
                reason = "NO_RECORD";
                return false;
            }
            if (value.AlgorithmVersion != AERISRunwaySurveySnapshot.CurrentAlgorithmVersion)
            {
                reason = "ALGORITHM " + value.AlgorithmVersion + " -> " +
                    AERISRunwaySurveySnapshot.CurrentAlgorithmVersion;
                return false;
            }
            if (string.IsNullOrEmpty(value.SourceFingerprint) ||
                string.IsNullOrEmpty(snapshot.SourceFingerprint) ||
                !string.Equals(value.SourceFingerprint, snapshot.SourceFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "SOURCE " + ShortHash(value.SourceFingerprint) + " -> " +
                    ShortHash(snapshot.SourceFingerprint);
                return false;
            }
            if (value.GeometryPointCount != snapshot.Points.Length ||
                value.GeometryPrimitiveCount != snapshot.Primitives.Length ||
                value.ColliderReadable != snapshot.ColliderReadable)
            {
                reason = "GEOMETRY SHAPE/COUNT CHANGED";
                return false;
            }
            if (!string.Equals(value.ProviderVersion, snapshot.ProviderVersion,
                StringComparison.OrdinalIgnoreCase))
            {
                reason = "PROVIDER VERSION CHANGED";
                return false;
            }
            double horizontal = HorizontalDistanceMeters(value.LatitudeDeg,
                value.LongitudeDeg, snapshot.ReferenceLatitudeDeg,
                snapshot.ReferenceLongitudeDeg, snapshot.BodyRadiusMeters);
            double vertical = Math.Abs(value.ElevationMeters -
                snapshot.ReferenceElevationMeters);
            double heading = HeadingDeltaDegrees(value.HeadingDeg,
                snapshot.DeclaredHeadingDeg);
            double liveScale = provider == null ? 1.0 : provider.RuntimeModelScale;
            double scale = Math.Abs(value.ModelScale - liveScale);
            if (!Finite(horizontal) || horizontal > MaximumHorizontalMeters ||
                !Finite(vertical) || vertical > MaximumVerticalMeters ||
                !Finite(heading) || heading > MaximumHeadingDegrees ||
                !Finite(scale) || scale > MaximumScaleDelta)
            {
                reason = "PLACEMENT DELTA h=" +
                    horizontal.ToString("0.000", CultureInfo.InvariantCulture) +
                    "m v=" + vertical.ToString("0.000", CultureInfo.InvariantCulture) +
                    "m hdg=" + heading.ToString("0.000", CultureInfo.InvariantCulture) +
                    "deg scale=" + scale.ToString("0.000000",
                        CultureInfo.InvariantCulture);
                return false;
            }
            record = Clone(value);
            reason = "SUB-METRE FRAME COMPATIBLE h=" +
                horizontal.ToString("0.000", CultureInfo.InvariantCulture) +
                "m v=" + vertical.ToString("0.000", CultureInfo.InvariantCulture) +
                "m hdg=" + heading.ToString("0.000", CultureInfo.InvariantCulture) +
                "deg";
            return true;
        }

        static double HorizontalDistanceMeters(double latitudeA, double longitudeA,
            double latitudeB, double longitudeB, double bodyRadiusMeters)
        {
            if (!Finite(latitudeA) || !Finite(longitudeA) || !Finite(latitudeB) ||
                !Finite(longitudeB)) return double.NaN;
            double radius = Finite(bodyRadiusMeters) && bodyRadiusMeters > 1000.0
                ? bodyRadiusMeters : 600000.0;
            const double radians = Math.PI / 180.0;
            double meanLatitude = (latitudeA + latitudeB) * 0.5 * radians;
            double north = (latitudeB - latitudeA) * radians * radius;
            double longitudeDelta = longitudeB - longitudeA;
            while (longitudeDelta > 180.0) longitudeDelta -= 360.0;
            while (longitudeDelta < -180.0) longitudeDelta += 360.0;
            double east = longitudeDelta * radians * radius * Math.Cos(meanLatitude);
            return Math.Sqrt(north * north + east * east);
        }

        static double HeadingDeltaDegrees(double a, double b)
        {
            if (!Finite(a) || !Finite(b)) return double.NaN;
            double delta = Math.Abs(a - b) % 360.0;
            return delta > 180.0 ? 360.0 - delta : delta;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string ShortHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return "EMPTY";
            return value.Length <= 12 ? value : value.Substring(0, 12);
        }

        internal bool TryGetLastKnownGood(string stableRecordId,
            out AERISCachedRunwayRecord record)
        {
            record = null;
            AERISCachedRunwayRecord value;
            if (string.IsNullOrEmpty(stableRecordId) ||
                !entries.TryGetValue(stableRecordId, out value)) return false;
            record = Clone(value);
            return true;
        }

        internal void Put(AERISRunwaySurveySnapshot snapshot,
            AERISProviderFacilityRecord provider, AERISAirfieldDefinition airfield)
        {
            if (snapshot == null || provider == null ||
                string.IsNullOrEmpty(snapshot.StableRecordId) ||
                string.IsNullOrEmpty(snapshot.InputFingerprint) ||
                airfield == null) return;
            string gameBuild = string.Empty;
            try { gameBuild = typeof(KSPUtil).Assembly.GetName().Version.ToString(); }
            catch { }
            entries[snapshot.StableRecordId] = new AERISCachedRunwayRecord
            {
                StableRecordId = snapshot.StableRecordId,
                Fingerprint = snapshot.InputFingerprint,
                SourceFingerprint = snapshot.SourceFingerprint,
                AlgorithmVersion = AERISRunwaySurveySnapshot.CurrentAlgorithmVersion,
                ProviderVersion = provider.ProviderVersion ?? string.Empty,
                Body = provider.Body ?? string.Empty,
                ProviderUuid = provider.ProviderUuid ?? string.Empty,
                ProviderSiteId = provider.ProviderSiteId ?? string.Empty,
                SourcePath = provider.SourcePath ?? string.Empty,
                ModelName = provider.ModelName ?? string.Empty,
                SourceMod = provider.SourceMod ?? string.Empty,
                LatitudeDeg = snapshot.ReferenceLatitudeDeg,
                LongitudeDeg = snapshot.ReferenceLongitudeDeg,
                ElevationMeters = snapshot.ReferenceElevationMeters,
                HeadingDeg = snapshot.DeclaredHeadingDeg,
                ModelScale = provider.RuntimeModelScale,
                GeometryPointCount = snapshot.Points.Length,
                GeometryPrimitiveCount = snapshot.Primitives.Length,
                ColliderReadable = snapshot.ColliderReadable,
                GameBuild = gameBuild,
                AerisBuild = global::AERISFlightControl.AERISBuildVersion.Semantic,
                SavedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Airfield = airfield.Clone()
            };
            failures.Remove(snapshot.StableRecordId);
        }

        internal void RecordFailure(AERISProviderFacilityRecord provider,
            string stableRecordId, string fingerprint,
            AERISRunwayCertificationState state, AERISRunwayFailureCode code,
            string detail)
        {
            if (string.IsNullOrEmpty(stableRecordId)) return;
            failures[stableRecordId] = new AERISCachedRunwayFailure
            {
                StableRecordId = stableRecordId,
                Fingerprint = fingerprint ?? string.Empty,
                AlgorithmVersion = AERISRunwaySurveySnapshot.CurrentAlgorithmVersion,
                State = state,
                Code = code,
                Detail = detail ?? string.Empty,
                ProviderVersion = provider == null ? string.Empty :
                    provider.ProviderVersion ?? string.Empty,
                Body = provider == null ? string.Empty : provider.Body ?? string.Empty,
                ProviderUuid = provider == null ? string.Empty :
                    provider.ProviderUuid ?? string.Empty,
                ProviderSiteId = provider == null ? string.Empty :
                    provider.ProviderSiteId ?? string.Empty,
                SourcePath = provider == null ? string.Empty :
                    provider.SourcePath ?? string.Empty,
                ModelName = provider == null ? string.Empty :
                    provider.ModelName ?? string.Empty,
                SavedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        internal bool Save(out string error)
        {
            error = string.Empty;
            string path = ResolvePath();
            string temporary = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                var root = new ConfigNode(CacheNodeName);
                root.AddValue("schemaVersion", CurrentSchemaVersion);
                root.AddValue("algorithmVersion",
                    AERISRunwaySurveySnapshot.CurrentAlgorithmVersion);
                root.AddValue("savedUtc", DateTime.UtcNow.ToString("o",
                    CultureInfo.InvariantCulture));
                var keys = new List<string>(entries.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < keys.Count; i++)
                    WriteRecord(root.AddNode("Record"), entries[keys[i]]);
                var failureKeys = new List<string>(failures.Keys);
                failureKeys.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < failureKeys.Count; i++)
                    WriteFailure(root.AddNode("FailureRecord"), failures[failureKeys[i]]);
                if (File.Exists(temporary)) File.Delete(temporary);
                root.Save(temporary);
                // Validate the temporary file before replacing the previous cache.
                ConfigNode verify = ConfigNode.Load(temporary);
                ConfigNode verifyRoot = ResolveCacheRoot(verify);
                if (verifyRoot == null ||
                    ReadInt(verifyRoot, "schemaVersion") != CurrentSchemaVersion)
                    throw new InvalidDataException("temporary cache cannot be read back");
                if (verifyRoot.GetNodes("Record").Length != entries.Count ||
                    verifyRoot.GetNodes("FailureRecord").Length != failures.Count)
                    throw new InvalidDataException("temporary cache record count mismatch");
                var roundTripEntries = new Dictionary<string, AERISCachedRunwayRecord>(
                    StringComparer.OrdinalIgnoreCase);
                var roundTripFailures = new Dictionary<string, AERISCachedRunwayFailure>(
                    StringComparer.OrdinalIgnoreCase);
                string roundTripError;
                bool roundTripLoaded = TryLoadFile(temporary, roundTripEntries, roundTripFailures,
                    out roundTripError);
                if (!roundTripLoaded || roundTripEntries.Count != entries.Count ||
                    roundTripFailures.Count != failures.Count)
                    throw new InvalidDataException("temporary cache full round-trip failed: " +
                        (roundTripLoaded ? "COUNT MISMATCH" : roundTripError) +
                        "; expected certified=" + entries.Count + ", failures=" +
                        failures.Count + "; actual certified=" + roundTripEntries.Count +
                        ", failures=" + roundTripFailures.Count);
                string backup = path + ".bak";
                if (File.Exists(path))
                {
                    try { File.Replace(temporary, path, backup, true); }
                    catch
                    {
                        File.Copy(path, backup, true);
                        File.Delete(path);
                        File.Move(temporary, path);
                    }
                }
                else File.Move(temporary, path);
                LastStatus = "CACHE SAVED " + entries.Count + " CERTIFIED / " +
                    failures.Count + " FAILURE RECORD(S)";
                AERISLogger.Info("[AIRFIELD_CACHE] save verified; certified=" +
                    entries.Count + "; failures=" + failures.Count +
                    "; fullRoundTrip=True.");
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                error = ex.GetType().Name + ": " + ex.Message;
                LastStatus = "CACHE SAVE FAILED: " + ex.GetType().Name;
                return false;
            }
        }

        // ConfigNode.Save can round-trip a named node either as the loaded root itself
        // or as direct values/nodes on a generic file root, depending on the KSP/Mono
        // implementation.  Older v0.17.0.2 caches used the direct-root shape and were
        // incorrectly rejected as CACHE ROOT MISSING.  One resolver is used for primary,
        // backup and post-write verification so both representations are durable.
        static ConfigNode ResolveCacheRoot(ConfigNode loaded)
        {
            if (loaded == null) return null;
            if (string.Equals(loaded.name, CacheNodeName,
                StringComparison.OrdinalIgnoreCase)) return loaded;
            ConfigNode named = loaded.GetNode(CacheNodeName);
            if (named != null) return named;
            string schema = loaded.GetValue("schemaVersion");
            return string.IsNullOrEmpty(schema) ? null : loaded;
        }

        static AERISCachedRunwayRecord Clone(AERISCachedRunwayRecord source)
        {
            return new AERISCachedRunwayRecord
            {
                StableRecordId = source.StableRecordId,
                Fingerprint = source.Fingerprint,
                SourceFingerprint = source.SourceFingerprint,
                AlgorithmVersion = source.AlgorithmVersion,
                ProviderVersion = source.ProviderVersion,
                Body = source.Body,
                ProviderUuid = source.ProviderUuid,
                ProviderSiteId = source.ProviderSiteId,
                SourcePath = source.SourcePath,
                ModelName = source.ModelName,
                SourceMod = source.SourceMod,
                LatitudeDeg = source.LatitudeDeg,
                LongitudeDeg = source.LongitudeDeg,
                ElevationMeters = source.ElevationMeters,
                HeadingDeg = source.HeadingDeg,
                ModelScale = source.ModelScale,
                GeometryPointCount = source.GeometryPointCount,
                GeometryPrimitiveCount = source.GeometryPrimitiveCount,
                ColliderReadable = source.ColliderReadable,
                GameBuild = source.GameBuild,
                AerisBuild = source.AerisBuild,
                SavedUtc = source.SavedUtc,
                Airfield = source.Airfield == null ? null : source.Airfield.Clone()
            };
        }

        static AERISCachedRunwayFailure Clone(AERISCachedRunwayFailure source)
        {
            return new AERISCachedRunwayFailure
            {
                StableRecordId = source.StableRecordId,
                Fingerprint = source.Fingerprint,
                AlgorithmVersion = source.AlgorithmVersion,
                State = source.State,
                Code = source.Code,
                Detail = source.Detail,
                ProviderVersion = source.ProviderVersion,
                Body = source.Body,
                ProviderUuid = source.ProviderUuid,
                ProviderSiteId = source.ProviderSiteId,
                SourcePath = source.SourcePath,
                ModelName = source.ModelName,
                SavedUtc = source.SavedUtc
            };
        }

        static AERISCachedRunwayRecord ParseRecord(ConfigNode node, int schemaVersion)
        {
            if (node == null) return null;
            var record = new AERISCachedRunwayRecord
            {
                StableRecordId = ReadStableRecordId(node),
                Fingerprint = Read(node, "fingerprint"),
                SourceFingerprint = Read(node, "sourceFingerprint"),
                AlgorithmVersion = ReadInt(node, "algorithmVersion"),
                ProviderVersion = Read(node, "providerVersion"),
                Body = Read(node, "body"),
                ProviderUuid = Read(node, "providerUuid"),
                ProviderSiteId = Read(node, "providerSiteId"),
                SourcePath = Read(node, "sourcePath"),
                ModelName = Read(node, "modelName"),
                SourceMod = Read(node, "sourceMod"),
                GeometryPointCount = ReadInt(node, "geometryPointCount"),
                GeometryPrimitiveCount = ReadInt(node, "geometryPrimitiveCount"),
                ColliderReadable = ReadBool(node, "colliderReadable"),
                GameBuild = Read(node, "gameBuild"),
                AerisBuild = Read(node, "aerisBuild"),
                SavedUtc = Read(node, "savedUtc")
            };
            ReadDouble(node, "latitude", out record.LatitudeDeg);
            ReadDouble(node, "longitude", out record.LongitudeDeg);
            ReadDouble(node, "elevation", out record.ElevationMeters);
            ReadDouble(node, "heading", out record.HeadingDeg);
            if (!ReadDouble(node, "modelScale", out record.ModelScale) ||
                record.ModelScale <= 0.0) record.ModelScale = 1.0;
            if (schemaVersion < CurrentSchemaVersion ||
                !IsPhysicalStableRecordId(record.StableRecordId))
            {
                string canonicalId = AERISProviderIdentity.ComposeStableRecordId(
                    record.Body, record.ProviderUuid, record.ProviderSiteId,
                    record.SourcePath, record.ModelName);
                if (!string.IsNullOrEmpty(canonicalId)) record.StableRecordId = canonicalId;
            }
            ConfigNode airfieldNode = node.GetNode("Airfield");
            if (airfieldNode == null) return null;
            var airfield = new AERISAirfieldDefinition
            {
                Id = Read(airfieldNode, "id"),
                Body = Read(airfieldNode, "body", "Kerbin"),
                DisplayName = Read(airfieldNode, "displayName", "UNNAMED"),
                Description = Read(airfieldNode, "description"),
                Source = ReadEnum(airfieldNode, "source", AERISAirfieldSource.Unknown),
                FacilityKind = AERISFacilityKind.Runway,
                Validation = AERISAirfieldValidation.PrecisionValidated,
                ProviderSiteId = Read(airfieldNode, "providerSiteId"),
                ProviderGroup = Read(airfieldNode, "providerGroup"),
                ProviderUuid = Read(airfieldNode, "providerUuid"),
                ProviderStableRecordId = Read(airfieldNode, "providerStableRecordId"),
                SourceMod = Read(airfieldNode, "sourceMod"),
                ProviderVersion = Read(airfieldNode, "providerVersion"),
                ProviderDetected = false,
                ProviderRuntimeStatus = "CERTIFIED CACHE"
            };
            ReadDouble(airfieldNode, "referenceLatitude", out airfield.ReferenceLatitudeDeg);
            ReadDouble(airfieldNode, "referenceLongitude", out airfield.ReferenceLongitudeDeg);
            ReadDouble(airfieldNode, "referenceElevation", out airfield.ReferenceElevationMeters);
            ConfigNode[] runways = airfieldNode.GetNodes("Runway");
            for (int i = 0; i < runways.Length; i++)
            {
                AERISRunwayDefinition runway = ParseRunway(runways[i], airfield);
                if (runway != null) airfield.Runways.Add(runway);
            }
            if (string.IsNullOrEmpty(airfield.Id) || airfield.Runways.Count == 0) return null;
            NormalizeCachedStableIds(record.StableRecordId, airfield);
            record.Airfield = airfield;
            return record;
        }

        static void NormalizeCachedStableIds(string stableRecordId,
            AERISAirfieldDefinition airfield)
        {
            if (airfield == null || string.IsNullOrEmpty(stableRecordId)) return;
            for (int i = 0; i < airfield.Runways.Count; i++)
            {
                AERISRunwayDefinition runway = airfield.Runways[i];
                if (runway == null) continue;
                runway.StableId = stableRecordId + "\n" + (runway.Id ?? string.Empty);
                for (int j = 0; j < runway.Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction = runway.Directions[j];
                    if (direction == null) continue;
                    direction.StableId = runway.StableId + "\n" +
                        (direction.Id ?? string.Empty);
                }
            }
        }

        static AERISCachedRunwayFailure ParseFailure(ConfigNode node, int schemaVersion)
        {
            if (node == null) return null;
            var value = new AERISCachedRunwayFailure
            {
                StableRecordId = ReadStableRecordId(node),
                Fingerprint = Read(node, "fingerprint"),
                AlgorithmVersion = ReadInt(node, "algorithmVersion"),
                State = ReadEnum(node, "state", AERISRunwayCertificationState.Failed),
                Code = ReadEnum(node, "code", AERISRunwayFailureCode.None),
                Detail = Read(node, "detail"),
                ProviderVersion = Read(node, "providerVersion"),
                Body = Read(node, "body"),
                ProviderUuid = Read(node, "providerUuid"),
                ProviderSiteId = Read(node, "providerSiteId"),
                SourcePath = Read(node, "sourcePath"),
                ModelName = Read(node, "modelName"),
                SavedUtc = Read(node, "savedUtc")
            };
            if (schemaVersion < CurrentSchemaVersion ||
                !IsPhysicalStableRecordId(value.StableRecordId))
            {
                string canonicalId = AERISProviderIdentity.ComposeStableRecordId(
                    value.Body, value.ProviderUuid, value.ProviderSiteId,
                    value.SourcePath, value.ModelName);
                if (!string.IsNullOrEmpty(canonicalId)) value.StableRecordId = canonicalId;
            }
            return string.IsNullOrEmpty(value.StableRecordId) ? null : value;
        }

        static AERISRunwayDefinition ParseRunway(ConfigNode node,
            AERISAirfieldDefinition airfield)
        {
            var runway = new AERISRunwayDefinition
            {
                Id = Read(node, "id"),
                DisplayName = Read(node, "displayName"),
                ProviderSiteId = Read(node, "providerSiteId"),
                ProviderUuid = Read(node, "providerUuid"),
                GeometryFingerprint = Read(node, "fingerprint"),
                GeometryRevision = ReadLong(node, "revision"),
                Surface = Read(node, "surface", "UNKNOWN")
            };
            runway.StableId = Read(node, "stableId");
            if (string.IsNullOrEmpty(runway.StableId))
                runway.StableId = airfield.StableId + "\n" + runway.Id;
            ReadDouble(node, "length", out runway.LengthMeters);
            ReadDouble(node, "width", out runway.WidthMeters);
            ConfigNode[] directions = node.GetNodes("Direction");
            for (int i = 0; i < directions.Length; i++)
            {
                AERISRunwayDirectionDefinition direction = ParseDirection(directions[i], runway);
                if (direction != null) runway.Directions.Add(direction);
            }
            ConfigNode[] polygon = node.GetNodes("UsablePoint");
            for (int i = 0; i < polygon.Length; i++)
            {
                AERISGeoPoint point = ParsePoint(polygon[i]);
                if (point != null) runway.UsablePolygon.Add(point);
            }
            string widths = Read(node, "widthProfile");
            if (!string.IsNullOrEmpty(widths))
            {
                string[] values = widths.Split(',');
                for (int i = 0; i < values.Length; i++)
                {
                    double width;
                    if (double.TryParse(values[i], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out width) && width > 0.0)
                        runway.WidthProfileMeters.Add(width);
                }
            }
            return string.IsNullOrEmpty(runway.Id) || runway.Directions.Count == 0 ? null : runway;
        }

        static AERISRunwayDirectionDefinition ParseDirection(ConfigNode node,
            AERISRunwayDefinition runway)
        {
            if (node == null) return null;
            var direction = new AERISRunwayDirectionDefinition
            {
                Id = Read(node, "id"),
                DisplayName = Read(node, "displayName"),
                StableId = Read(node, "stableId"),
                CertificationState = ReadEnum(node, "state",
                    AERISRunwayCertificationState.Pending),
                FailureCode = ReadEnum(node, "failureCode", AERISRunwayFailureCode.None),
                FailureDetail = Read(node, "failureDetail"),
                PendingDetail = Read(node, "pendingDetail"),
                CertificationBasis = ReadEnum(node, "certificationBasis",
                    AERISRunwayCertificationBasis.Unknown),
                CertificationBasisDetail = Read(node, "certificationBasisDetail"),
                EvidenceFamilies = (AERISRunwayEvidenceFamily)ReadInt(node, "evidenceFamilies"),
                MeasurementMethods = (AERISRunwayMeasurementMethod)ReadLong(node, "methods"),
                GeometryFingerprint = Read(node, "fingerprint"),
                GeometryRevision = ReadLong(node, "revision"),
                CertifiedUtc = Read(node, "certifiedUtc"),
                Threshold = ParsePoint(node.GetNode("OperationalThreshold")),
                OppositeThreshold = ParsePoint(node.GetNode("OppositeOperationalEnd")),
                PhysicalStart = ParsePoint(node.GetNode("PhysicalStart")),
                PhysicalEnd = ParsePoint(node.GetNode("PhysicalEnd")),
                UsableStart = ParsePoint(node.GetNode("UsableStart")),
                UsableEnd = ParsePoint(node.GetNode("UsableEnd")),
                TouchdownAim = ParsePoint(node.GetNode("TouchdownAim")),
                RolloutEnd = ParsePoint(node.GetNode("RolloutEnd"))
            };
            if (string.IsNullOrEmpty(direction.StableId))
                direction.StableId = runway.StableId + "\n" + direction.Id;
            ReadDouble(node, "heading", out direction.HeadingDeg);
            ReadDouble(node, "glidePathAngle", out direction.GlidePathAngleDeg);
            ReadDouble(node, "tch", out direction.ThresholdCrossingHeightMeters);
            if (direction.GlidePathAngleDeg <= 0.0 || direction.GlidePathAngleDeg > 10.0)
                direction.GlidePathAngleDeg = 3.0;
            if (direction.ThresholdCrossingHeightMeters <= 0.0 ||
                direction.ThresholdCrossingHeightMeters > 100.0)
                direction.ThresholdCrossingHeightMeters = 15.0;
            ReadDouble(node, "classificationConfidence", out direction.ClassificationConfidence);
            ReadDouble(node, "geometryConfidence", out direction.GeometryConfidence);
            ReadDouble(node, "centerlineUncertainty", out direction.CenterlineUncertaintyMeters);
            ReadDouble(node, "headingUncertainty", out direction.HeadingUncertaintyDeg);
            ReadDouble(node, "physicalEndUncertainty",
                out direction.PhysicalEndUncertaintyMeters);
            ReadDouble(node, "usableEndUncertainty",
                out direction.UsableEndUncertaintyMeters);
            ReadDouble(node, "thresholdUncertainty", out direction.ThresholdUncertaintyMeters);
            ReadDouble(node, "lengthUncertainty", out direction.LengthUncertaintyMeters);
            ReadDouble(node, "widthUncertainty", out direction.WidthUncertaintyMeters);
            ReadDouble(node, "elevationUncertainty", out direction.ElevationUncertaintyMeters);
            ReadDouble(node, "displacedThresholdConfidence",
                out direction.DisplacedThresholdConfidence);
            ReadDouble(node, "approachCorridorConfidence",
                out direction.ApproachCorridorConfidence);
            direction.LocalizerCaptureAngleDeg = 25.0;
            direction.LocalizerCaptureDistanceMeters = 30000.0;
            direction.GlidePathCaptureDistanceMeters = 20000.0;
            direction.MissedApproachHeadingDeg = direction.HeadingDeg;
            direction.MissedApproachSafeAltitudeMeters = 1000.0;
            ConfigNode[] estimates = node.GetNodes("Parameter");
            for (int i = 0; i < estimates.Length; i++)
            {
                AERISRunwayParameterEstimate estimate = ParseEstimate(estimates[i]);
                if (estimate != null) direction.ParameterEstimates.Add(estimate);
            }
            direction.PopulateOperationalReferences(Math.Min(300.0,
                Math.Max(60.0, runway.LengthMeters * 0.12)));
            return string.IsNullOrEmpty(direction.Id) || !direction.HasFiniteGeometry
                ? null : direction;
        }

        static void WriteRecord(ConfigNode node, AERISCachedRunwayRecord record)
        {
            WriteStableRecordId(node, record.StableRecordId);
            Add(node, "fingerprint", record.Fingerprint);
            Add(node, "sourceFingerprint", record.SourceFingerprint);
            Add(node, "algorithmVersion", record.AlgorithmVersion);
            Add(node, "providerVersion", record.ProviderVersion);
            Add(node, "body", record.Body);
            Add(node, "providerUuid", record.ProviderUuid);
            Add(node, "providerSiteId", record.ProviderSiteId);
            Add(node, "sourcePath", record.SourcePath);
            Add(node, "modelName", record.ModelName);
            Add(node, "sourceMod", record.SourceMod);
            Add(node, "latitude", record.LatitudeDeg);
            Add(node, "longitude", record.LongitudeDeg);
            Add(node, "elevation", record.ElevationMeters);
            Add(node, "heading", record.HeadingDeg);
            Add(node, "modelScale", record.ModelScale);
            Add(node, "geometryPointCount", record.GeometryPointCount);
            Add(node, "geometryPrimitiveCount", record.GeometryPrimitiveCount);
            Add(node, "colliderReadable", record.ColliderReadable);
            Add(node, "gameBuild", record.GameBuild);
            Add(node, "aerisBuild", record.AerisBuild);
            Add(node, "savedUtc", record.SavedUtc);
            ConfigNode airfield = node.AddNode("Airfield");
            AERISAirfieldDefinition value = record.Airfield;
            Add(airfield, "id", value.Id);
            Add(airfield, "body", value.Body);
            Add(airfield, "displayName", value.DisplayName);
            Add(airfield, "description", value.Description);
            Add(airfield, "source", value.Source);
            Add(airfield, "providerSiteId", value.ProviderSiteId);
            Add(airfield, "providerGroup", value.ProviderGroup);
            Add(airfield, "providerUuid", value.ProviderUuid);
            Add(airfield, "providerStableRecordId", value.ProviderStableRecordId);
            Add(airfield, "sourceMod", value.SourceMod);
            Add(airfield, "providerVersion", value.ProviderVersion);
            Add(airfield, "referenceLatitude", value.ReferenceLatitudeDeg);
            Add(airfield, "referenceLongitude", value.ReferenceLongitudeDeg);
            Add(airfield, "referenceElevation", value.ReferenceElevationMeters);
            for (int i = 0; i < value.Runways.Count; i++)
                WriteRunway(airfield.AddNode("Runway"), value.Runways[i]);
        }

        static void WriteFailure(ConfigNode node, AERISCachedRunwayFailure value)
        {
            WriteStableRecordId(node, value.StableRecordId);
            Add(node, "fingerprint", value.Fingerprint);
            Add(node, "algorithmVersion", value.AlgorithmVersion);
            Add(node, "state", value.State);
            Add(node, "code", value.Code);
            Add(node, "detail", value.Detail);
            Add(node, "providerVersion", value.ProviderVersion);
            Add(node, "body", value.Body);
            Add(node, "providerUuid", value.ProviderUuid);
            Add(node, "providerSiteId", value.ProviderSiteId);
            Add(node, "sourcePath", value.SourcePath);
            Add(node, "modelName", value.ModelName);
            Add(node, "savedUtc", value.SavedUtc);
        }


        static bool IsPhysicalStableRecordId(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf("\nPHYSICAL_RUNWAY\n",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void WriteStableRecordId(ConfigNode node, string stableRecordId)
        {
            string value = stableRecordId ?? string.Empty;
            Add(node, "stableRecordIdEncoding", StableIdEncoding);
            Add(node, "stableRecordIdB64", Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value)));
            Add(node, "stableRecordId", EscapeStableRecordId(value));
        }

        static string ReadStableRecordId(ConfigNode node)
        {
            if (node == null) return string.Empty;
            string encoding = Read(node, "stableRecordIdEncoding");
            string encoded = Read(node, "stableRecordIdB64");
            if (string.Equals(encoding, StableIdEncoding,
                StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(encoded))
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
                catch { }
            }
            return Read(node, "stableRecordId");
        }

        static string EscapeStableRecordId(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static bool PreferCandidate(AERISCachedRunwayRecord existing,
            AERISCachedRunwayRecord candidate)
        {
            if (existing == null) return true;
            if (candidate == null) return false;
            if (candidate.AlgorithmVersion != existing.AlgorithmVersion)
                return candidate.AlgorithmVersion > existing.AlgorithmVersion;
            return CompareSavedUtc(candidate.SavedUtc, existing.SavedUtc) > 0;
        }

        static bool PreferCandidate(AERISCachedRunwayFailure existing,
            AERISCachedRunwayFailure candidate)
        {
            if (existing == null) return true;
            if (candidate == null) return false;
            if (candidate.AlgorithmVersion != existing.AlgorithmVersion)
                return candidate.AlgorithmVersion > existing.AlgorithmVersion;
            return CompareSavedUtc(candidate.SavedUtc, existing.SavedUtc) > 0;
        }

        static int CompareSavedUtc(string left, string right)
        {
            DateTime leftValue;
            DateTime rightValue;
            bool leftParsed = DateTime.TryParse(left, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out leftValue);
            bool rightParsed = DateTime.TryParse(right, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out rightValue);
            if (leftParsed && rightParsed) return leftValue.CompareTo(rightValue);
            if (leftParsed) return 1;
            if (rightParsed) return -1;
            return string.Compare(left ?? string.Empty, right ?? string.Empty,
                StringComparison.Ordinal);
        }

        static void WriteRunway(ConfigNode node, AERISRunwayDefinition runway)
        {
            Add(node, "id", runway.Id);
            Add(node, "stableId", runway.StableId);
            Add(node, "displayName", runway.DisplayName);
            Add(node, "providerSiteId", runway.ProviderSiteId);
            Add(node, "providerUuid", runway.ProviderUuid);
            Add(node, "length", runway.LengthMeters);
            Add(node, "width", runway.WidthMeters);
            Add(node, "surface", runway.Surface);
            Add(node, "fingerprint", runway.GeometryFingerprint);
            Add(node, "revision", runway.GeometryRevision);
            if (runway.WidthProfileMeters.Count > 0)
            {
                var values = new string[runway.WidthProfileMeters.Count];
                for (int i = 0; i < values.Length; i++) values[i] =
                    runway.WidthProfileMeters[i].ToString("0.###", CultureInfo.InvariantCulture);
                Add(node, "widthProfile", string.Join(",", values));
            }
            for (int i = 0; i < runway.UsablePolygon.Count; i++)
                WritePoint(node.AddNode("UsablePoint"), runway.UsablePolygon[i]);
            for (int i = 0; i < runway.Directions.Count; i++)
                WriteDirection(node.AddNode("Direction"), runway.Directions[i]);
        }

        static void WriteDirection(ConfigNode node, AERISRunwayDirectionDefinition direction)
        {
            Add(node, "id", direction.Id);
            Add(node, "stableId", direction.StableId);
            Add(node, "displayName", direction.DisplayName);
            Add(node, "state", direction.CertificationState);
            Add(node, "failureCode", direction.FailureCode);
            Add(node, "failureDetail", direction.FailureDetail);
            Add(node, "pendingDetail", direction.PendingDetail);
            Add(node, "certificationBasis", direction.CertificationBasis);
            Add(node, "certificationBasisDetail", direction.CertificationBasisDetail);
            Add(node, "heading", direction.HeadingDeg);
            Add(node, "glidePathAngle", direction.GlidePathAngleDeg);
            Add(node, "tch", direction.ThresholdCrossingHeightMeters);
            Add(node, "classificationConfidence", direction.ClassificationConfidence);
            Add(node, "geometryConfidence", direction.GeometryConfidence);
            Add(node, "centerlineUncertainty", direction.CenterlineUncertaintyMeters);
            Add(node, "headingUncertainty", direction.HeadingUncertaintyDeg);
            Add(node, "physicalEndUncertainty", direction.PhysicalEndUncertaintyMeters);
            Add(node, "usableEndUncertainty", direction.UsableEndUncertaintyMeters);
            Add(node, "thresholdUncertainty", direction.ThresholdUncertaintyMeters);
            Add(node, "lengthUncertainty", direction.LengthUncertaintyMeters);
            Add(node, "widthUncertainty", direction.WidthUncertaintyMeters);
            Add(node, "elevationUncertainty", direction.ElevationUncertaintyMeters);
            Add(node, "displacedThresholdConfidence",
                direction.DisplacedThresholdConfidence);
            Add(node, "approachCorridorConfidence", direction.ApproachCorridorConfidence);
            Add(node, "evidenceFamilies", (int)direction.EvidenceFamilies);
            Add(node, "methods", (long)direction.MeasurementMethods);
            Add(node, "fingerprint", direction.GeometryFingerprint);
            Add(node, "revision", direction.GeometryRevision);
            Add(node, "certifiedUtc", direction.CertifiedUtc);
            WritePoint(node.AddNode("OperationalThreshold"), direction.Threshold);
            WritePoint(node.AddNode("OppositeOperationalEnd"), direction.OppositeThreshold);
            WritePoint(node.AddNode("PhysicalStart"), direction.PhysicalStart);
            WritePoint(node.AddNode("PhysicalEnd"), direction.PhysicalEnd);
            WritePoint(node.AddNode("UsableStart"), direction.UsableStart);
            WritePoint(node.AddNode("UsableEnd"), direction.UsableEnd);
            WritePoint(node.AddNode("TouchdownAim"), direction.TouchdownAim);
            WritePoint(node.AddNode("RolloutEnd"), direction.RolloutEnd);
            for (int i = 0; i < direction.ParameterEstimates.Count; i++)
                WriteEstimate(node.AddNode("Parameter"), direction.ParameterEstimates[i]);
        }

        static AERISRunwayParameterEstimate ParseEstimate(ConfigNode node)
        {
            if (node == null) return null;
            var value = new AERISRunwayParameterEstimate
            {
                Name = Read(node, "name"),
                Units = Read(node, "units"),
                EvidenceFamilies = (AERISRunwayEvidenceFamily)ReadInt(node,
                    "evidenceFamilies"),
                Methods = (AERISRunwayMeasurementMethod)ReadLong(node, "methods"),
                Detail = Read(node, "detail")
            };
            if (string.IsNullOrEmpty(value.Name) ||
                !ReadDouble(node, "value", out value.Value) ||
                !ReadDouble(node, "uncertainty", out value.Uncertainty) ||
                !ReadDouble(node, "confidence", out value.Confidence)) return null;
            return value;
        }

        static void WriteEstimate(ConfigNode node, AERISRunwayParameterEstimate value)
        {
            if (node == null || value == null) return;
            Add(node, "name", value.Name);
            Add(node, "units", value.Units);
            Add(node, "value", value.Value);
            Add(node, "uncertainty", value.Uncertainty);
            Add(node, "confidence", value.Confidence);
            Add(node, "evidenceFamilies", (int)value.EvidenceFamilies);
            Add(node, "methods", (long)value.Methods);
            Add(node, "detail", value.Detail);
        }

        static void WritePoint(ConfigNode node, AERISGeoPoint point)
        {
            if (node == null || point == null) return;
            Add(node, "lat", point.LatitudeDeg);
            Add(node, "lon", point.LongitudeDeg);
            Add(node, "elevation", point.ElevationMeters);
        }

        static AERISGeoPoint ParsePoint(ConfigNode node)
        {
            if (node == null) return null;
            var point = new AERISGeoPoint();
            return ReadDouble(node, "lat", out point.LatitudeDeg) &&
                ReadDouble(node, "lon", out point.LongitudeDeg) &&
                ReadDouble(node, "elevation", out point.ElevationMeters) && point.IsFinite
                ? point : null;
        }

        static string Read(ConfigNode node, string key, string fallback = "")
        {
            return node != null && node.HasValue(key) ? node.GetValue(key) ?? fallback : fallback;
        }

        static int ReadInt(ConfigNode node, string key)
        {
            int value;
            return int.TryParse(Read(node, key), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        static long ReadLong(ConfigNode node, string key)
        {
            long value;
            return long.TryParse(Read(node, key), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : 0L;
        }

        static bool ReadBool(ConfigNode node, string key)
        {
            bool value;
            return bool.TryParse(Read(node, key), out value) && value;
        }

        static bool ReadDouble(ConfigNode node, string key, out double value)
        {
            return double.TryParse(Read(node, key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static T ReadEnum<T>(ConfigNode node, string key, T fallback) where T : struct
        {
            T value;
            return Enum.TryParse(Read(node, key), true, out value) ? value : fallback;
        }

        static void Add(ConfigNode node, string key, object value)
        {
            if (node == null) return;
            string text;
            if (value is double) text = ((double)value).ToString("R", CultureInfo.InvariantCulture);
            else if (value is float) text = ((float)value).ToString("R", CultureInfo.InvariantCulture);
            else text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            node.AddValue(key, text);
        }

        static string ResolvePath()
        {
            return Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath,
                RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
