using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISRunwayWitness
    {
        internal string Body = string.Empty;
        internal string Name = string.Empty;
        internal string Source = string.Empty;
        internal string SourcePath = string.Empty;
        internal string ProviderStableRecordId = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderSiteId = string.Empty;
        internal string ProviderSourcePath = string.Empty;
        internal bool UserCalibrated;
        internal bool HasStart;
        internal bool HasEnd;
        internal AERISGeoPoint Start = new AERISGeoPoint();
        internal AERISGeoPoint End = new AERISGeoPoint();
        internal double HeadingDeg;
        internal double LengthMeters;
        internal double Confidence;
        internal double MatchDistanceMeters;
        internal string Fingerprint = string.Empty;
        internal bool PlacementMismatchObserved;
        internal double ObservedCrossTrackMeters;
        internal double ObservedAlongTrackMeters;
        internal double ObservedCorridorGateMeters;
        internal string PlacementObservationDetail = string.Empty;

        internal bool IsUsable
        {
            get
            {
                return HasStart && HasEnd && Start != null && End != null &&
                    Start.IsFinite && End.IsFinite && LengthMeters >= 80.0 &&
                    !double.IsNaN(HeadingDeg) && !double.IsInfinity(HeadingDeg);
            }
        }

        internal double ReciprocalHeadingDeg
        {
            get
            {
                return AERISAirfieldConfigParser.NormalizeHeading(HeadingDeg + 180.0);
            }
        }

        internal bool HasReciprocalDirectionPair
        {
            get { return IsUsable; }
        }

        internal AERISRunwayWitness Clone()
        {
            var value = (AERISRunwayWitness)MemberwiseClone();
            value.Start = Start == null ? null : Start.Clone();
            value.End = End == null ? null : End.Clone();
            return value;
        }
    }

    // Runway witness bridge. It reads Kramax plans from the user's installed mod
    // without copying or redistributing those files, and owns AERIS user calibrations.
    // The output is data-only and has no flight-control authority.
    internal sealed class AERISRunwayWitnessLibrary
    {
        const string KramaxRelativeDirectory = "GameData/KramaxAutoPilot";
        const string CalibrationRelativePath =
            "GameData/AERISFlightControl/PluginData/UserRunwayCalibrations.cfg";
        const string DefaultCalibrationRelativePath =
            "GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg";
        const int CalibrationSchema = 3;
        readonly List<AERISRunwayWitness> kramax = new List<AERISRunwayWitness>();
        readonly List<AERISRunwayWitness> user = new List<AERISRunwayWitness>();

        internal int KramaxCount { get { return kramax.Count; } }
        internal int UserCalibrationCount { get { return user.Count; } }
        internal string Status { get; private set; } = "NOT LOADED";
        internal string CalibrationStatus { get; private set; } = "NO CALIBRATION ACTION";

        internal void Reload()
        {
            kramax.Clear();
            user.Clear();
            LoadUserCalibrations();
            LoadKramaxPlans();
            Status = "KRAMAX " + kramax.Count + " / USER " + user.Count;
            RefreshCalibrationStatusFromStoredState();
            AERISLogger.Info("[RUNWAY_WITNESS] " + Status +
                "; external plans are read-only evidence and never guidance authority.");
        }

        internal bool HasUsableCalibration(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return false;
            for (int i = 0; i < user.Count; i++)
            {
                AERISRunwayWitness value = user[i];
                if (value != null && value.IsUsable && MatchesAirfieldIdentity(value, airfield))
                    return true;
            }
            return false;
        }

        void RefreshCalibrationStatusFromStoredState()
        {
            int ready = 0;
            int incomplete = 0;
            int quarantined = 0;
            for (int i = 0; i < user.Count; i++)
            {
                AERISRunwayWitness value = user[i];
                if (value == null) continue;
                if (value.IsUsable) ready++;
                else if (value.PlacementMismatchObserved) quarantined++;
                else incomplete++;
            }
            if (ready > 0)
                CalibrationStatus = "COMMITTED USER RUNWAY(S) " + ready +
                    " — RECIPROCAL PAIRS READY";
            else if (quarantined > 0)
                CalibrationStatus = "PLACEMENT MISMATCH QUARANTINE(S) " + quarantined +
                    " — USER TWO-POINT CALIBRATION REQUIRED";
            else if (incomplete > 0)
                CalibrationStatus = "INCOMPLETE USER CALIBRATION(S) " + incomplete +
                    " — SECOND THRESHOLD REQUIRED";
            else
                CalibrationStatus = "NO USER CALIBRATION";
        }

        internal AERISRunwayWitness Match(AERISProviderFacilityRecord record)
        {
            if (record == null || record.FacilityKind != AERISFacilityKind.Runway)
                return null;
            string stableId = AERISProviderIdentity.StableRecordId(record);
            AERISRunwayWitness exact = null;
            for (int i = 0; i < user.Count; i++)
            {
                AERISRunwayWitness candidate = user[i];
                if (candidate == null) continue;
                bool stable = !string.IsNullOrEmpty(candidate.ProviderStableRecordId) &&
                    string.Equals(candidate.ProviderStableRecordId, stableId,
                        StringComparison.OrdinalIgnoreCase);
                bool provider = string.Equals(candidate.Body, record.Body,
                        StringComparison.OrdinalIgnoreCase) &&
                    ((!string.IsNullOrEmpty(candidate.ProviderUuid) &&
                      string.Equals(candidate.ProviderUuid, record.ProviderUuid,
                          StringComparison.OrdinalIgnoreCase)) ||
                     (!string.IsNullOrEmpty(candidate.ProviderSiteId) &&
                      string.Equals(candidate.ProviderSiteId, record.ProviderSiteId,
                          StringComparison.OrdinalIgnoreCase)));
                if (stable || provider)
                {
                    exact = candidate.Clone();
                    exact.MatchDistanceMeters = 0.0;
                    return exact;
                }
            }

            if (!Finite(record.LatitudeDeg) || !Finite(record.LongitudeDeg)) return null;
            double radius = record.RuntimeBody == null || record.RuntimeBody.Radius <= 0.0
                ? 600000.0 : record.RuntimeBody.Radius;
            double bestScore = double.NegativeInfinity;
            AERISRunwayWitness best = null;
            for (int i = 0; i < kramax.Count; i++)
            {
                AERISRunwayWitness candidate = kramax[i];
                if (candidate == null || !candidate.IsUsable ||
                    !string.Equals(candidate.Body, record.Body,
                        StringComparison.OrdinalIgnoreCase)) continue;
                AERISGeoPoint midpoint = Midpoint(candidate.Start, candidate.End);
                var provider = new AERISGeoPoint
                {
                    LatitudeDeg = record.LatitudeDeg,
                    LongitudeDeg = NormalizeLongitude(record.LongitudeDeg),
                    ElevationMeters = record.ElevationMeters
                };
                double distance = AERISAirfieldConfigParser.GreatCircleDistanceMeters(
                    provider, midpoint, radius);
                if (!Finite(distance) || distance > 8000.0) continue;
                double token = NameAffinity(candidate.Name,
                    record.DisplayName + " " + record.ProviderSiteId + " " +
                    record.ProviderGroup + " " + record.ModelName);
                // Position is primary; names resolve nearby parallel/duplicate sites.
                double score = -distance + token * 1000.0;
                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
                best.MatchDistanceMeters = distance;
            }
            return best == null ? null : best.Clone();
        }

        internal bool MarkCalibration(AERISAirfieldDefinition airfield, bool start,
            Vessel vessel, out string detail)
        {
            detail = string.Empty;
            if (airfield == null)
            {
                detail = "AIRFIELD MISSING";
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
                detail = "CALIBRATION REQUIRES A VESSEL PARKED ON THE PHYSICAL RUNWAY";
                return false;
            }
            if (!Finite(vessel.srfSpeed) || vessel.srfSpeed > 5.0)
            {
                detail = "STOP THE VESSEL BEFORE MARKING A RUNWAY THRESHOLD";
                return false;
            }
            bool created;
            AERISRunwayWitness calibration = FindOrCreateCalibration(airfield, out created);
            AERISRunwayWitness backup = created ? null : calibration.Clone();
            var point = new AERISGeoPoint
            {
                LatitudeDeg = vessel.latitude,
                LongitudeDeg = NormalizeLongitude(vessel.longitude),
                ElevationMeters = Math.Max(-10000.0, Math.Min(1000000.0, vessel.altitude))
            };
            if (!point.IsFinite)
            {
                detail = "VESSEL GEO POSITION INVALID";
                return false;
            }
            if (start)
            {
                calibration.Start = point;
                calibration.HasStart = true;
            }
            else
            {
                calibration.End = point;
                calibration.HasEnd = true;
            }
            FinalizeWitness(calibration, BodyRadius(vessel.mainBody));
            if (calibration.HasStart && calibration.HasEnd &&
                calibration.LengthMeters < 80.0)
            {
                detail = "CALIBRATION ENDPOINTS ARE TOO CLOSE (" +
                    calibration.LengthMeters.ToString("0.0", CultureInfo.InvariantCulture) +
                    " M)";
                RollbackCalibration(calibration, created, backup);
                return false;
            }
            if (calibration.IsUsable)
            {
                calibration.PlacementMismatchObserved = false;
                calibration.ObservedCrossTrackMeters = 0.0;
                calibration.ObservedAlongTrackMeters = 0.0;
                calibration.ObservedCorridorGateMeters = 0.0;
                calibration.PlacementObservationDetail = string.Empty;
            }
            string error;
            if (!SaveUserCalibrations(out error))
            {
                RollbackCalibration(calibration, created, backup);
                detail = error;
                return false;
            }
            CalibrationStatus = (start ? "THRESHOLD A" : "THRESHOLD B") +
                " MARKED FOR " + airfield.DisplayName +
                (calibration.IsUsable
                    ? " — PHYSICAL RUNWAY " + RunwayPairLabel(calibration) +
                      " READY; BOTH RECIPROCAL DIRECTIONS WILL BE RESURVEYED"
                    : " — SECOND POINT REQUIRED");
            detail = CalibrationStatus;
            AERISLogger.Info("[RUNWAY_CALIBRATION] " + detail + "; lat=" +
                point.LatitudeDeg.ToString("0.00000000", CultureInfo.InvariantCulture) +
                "; lon=" + point.LongitudeDeg.ToString("0.00000000",
                    CultureInfo.InvariantCulture) +
                "; coordinateFrame=BODY_FIXED_GEODETIC_ABSOLUTE" +
                "; reciprocalPair=" + calibration.HasReciprocalDirectionPair +
                (calibration.IsUsable ? "; directionA=" +
                    RunwayNumber(calibration.HeadingDeg) + "; directionB=" +
                    RunwayNumber(calibration.ReciprocalHeadingDeg) : string.Empty) + ".");
            return true;
        }

        internal bool RecordPlacementMismatch(AERISAirfieldDefinition airfield,
            Vessel vessel, double crossTrackMeters, double alongTrackMeters,
            double corridorGateMeters, string observation, out string detail)
        {
            detail = string.Empty;
            if (airfield == null)
            {
                detail = "AIRFIELD MISSING";
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
            bool created;
            AERISRunwayWitness calibration = FindOrCreateCalibration(airfield, out created);
            if (!created && calibration != null && calibration.IsUsable)
            {
                detail = "COMPLETE USER A/B CALIBRATION PRESERVED — AUTOMATIC " +
                    "PLACEMENT QUARANTINE MAY NOT CLEAR MANUAL ENDPOINTS; USE CLEAR " +
                    "EXPLICITLY BEFORE RE-CALIBRATION";
                AERISLogger.Warn("[RUNWAY_PLACEMENT_VERIFY] site=" +
                    airfield.ProviderSiteId +
                    "; result=MANUAL_CALIBRATION_PRESERVED; automaticQuarantine=False; " +
                    "crossTrack=" + crossTrackMeters.ToString("0.00",
                        CultureInfo.InvariantCulture) + "m; alongTrack=" +
                    alongTrackMeters.ToString("0.00", CultureInfo.InvariantCulture) +
                    "m.");
                return false;
            }
            AERISRunwayWitness backup = created ? null : calibration.Clone();
            // A witnessed offset invalidates an uncalibrated automatic candidate only.
            // A complete manual A/B pair is protected by the guard above.
            // Keeping stale endpoints would let a usable witness bypass the quarantine.
            calibration.HasStart = false;
            calibration.HasEnd = false;
            calibration.Start = new AERISGeoPoint();
            calibration.End = new AERISGeoPoint();
            calibration.HeadingDeg = 0.0;
            calibration.LengthMeters = 0.0;
            calibration.Fingerprint = string.Empty;
            calibration.PlacementMismatchObserved = true;
            calibration.ObservedCrossTrackMeters = crossTrackMeters;
            calibration.ObservedAlongTrackMeters = alongTrackMeters;
            calibration.ObservedCorridorGateMeters = corridorGateMeters;
            calibration.PlacementObservationDetail = observation ?? string.Empty;
            string error;
            if (!SaveUserCalibrations(out error))
            {
                RollbackCalibration(calibration, created, backup);
                detail = error;
                return false;
            }
            CalibrationStatus = "PLACEMENT MISMATCH QUARANTINED FOR " +
                airfield.DisplayName + " — USER TWO-POINT CALIBRATION REQUIRED";
            detail = CalibrationStatus;
            AERISLogger.Warn("[RUNWAY_PLACEMENT_VERIFY] " + detail +
                "; crossTrack=" + crossTrackMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) + "m; alongTrack=" +
                alongTrackMeters.ToString("0.00", CultureInfo.InvariantCulture) +
                "m; gate=" + corridorGateMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) + "m; observation=" +
                calibration.PlacementObservationDetail + ".");
            return true;
        }

        internal bool ClearCalibration(AERISAirfieldDefinition airfield, out string detail)
        {
            detail = string.Empty;
            if (airfield == null)
            {
                detail = "AIRFIELD MISSING";
                return false;
            }
            var backup = new List<AERISRunwayWitness>(user.Count);
            for (int i = 0; i < user.Count; i++)
                backup.Add(user[i] == null ? null : user[i].Clone());
            int removed = 0;
            for (int i = user.Count - 1; i >= 0; i--)
            {
                AERISRunwayWitness value = user[i];
                if (!MatchesAirfieldIdentity(value, airfield)) continue;
                user.RemoveAt(i);
                removed++;
            }
            string error;
            if (!SaveUserCalibrations(out error))
            {
                user.Clear();
                user.AddRange(backup);
                detail = error;
                return false;
            }
            CalibrationStatus = removed > 0
                ? "CALIBRATION CLEARED FOR " + airfield.DisplayName
                : "NO CALIBRATION STORED FOR " + airfield.DisplayName;
            detail = CalibrationStatus;
            AERISLogger.Info("[RUNWAY_CALIBRATION] " + detail + ".");
            return true;
        }

        internal string CalibrationSummary(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return "NO AIRFIELD";
            for (int i = 0; i < user.Count; i++)
            {
                AERISRunwayWitness value = user[i];
                if (value == null) continue;
                if (!MatchesAirfieldIdentity(value, airfield)) continue;
                if (value.PlacementMismatchObserved && !value.IsUsable)
                    return "PLACEMENT MISMATCH — TWO-POINT CAL REQUIRED; A=" +
                        value.HasStart + " B=" + value.HasEnd + " CROSS " +
                        value.ObservedCrossTrackMeters.ToString("0.0",
                            CultureInfo.InvariantCulture) + "M / GATE " +
                        value.ObservedCorridorGateMeters.ToString("0.0",
                            CultureInfo.InvariantCulture) + "M";
                return "USER CAL A=" + value.HasStart + " B=" + value.HasEnd +
                    (value.IsUsable ? " PHYSICAL " + RunwayPairLabel(value) +
                        " LEN " + value.LengthMeters.ToString("0.0",
                        CultureInfo.InvariantCulture) + "M HDG " +
                        value.HeadingDeg.ToString("000.0", CultureInfo.InvariantCulture) +
                        "/" + value.ReciprocalHeadingDeg.ToString("000.0",
                            CultureInfo.InvariantCulture) +
                        " RECIPROCAL LOCALIZER PAIR" : string.Empty);
            }
            return "NO USER CALIBRATION";
        }

        internal string CalibrationEndpointSummary(AERISAirfieldDefinition airfield)
        {
            if (airfield == null) return "A/B ABSOLUTE GEO: NO AIRFIELD";
            for (int i = 0; i < user.Count; i++)
            {
                AERISRunwayWitness value = user[i];
                if (value == null || !MatchesAirfieldIdentity(value, airfield)) continue;
                string a = value.HasStart && value.Start != null && value.Start.IsFinite
                    ? "A LAT " + value.Start.LatitudeDeg.ToString("0.00000000", CultureInfo.InvariantCulture) +
                      " LON " + value.Start.LongitudeDeg.ToString("0.00000000", CultureInfo.InvariantCulture) +
                      " ALT " + value.Start.ElevationMeters.ToString("0.00", CultureInfo.InvariantCulture) + "M"
                    : "A NOT MARKED";
                string b = value.HasEnd && value.End != null && value.End.IsFinite
                    ? "B LAT " + value.End.LatitudeDeg.ToString("0.00000000", CultureInfo.InvariantCulture) +
                      " LON " + value.End.LongitudeDeg.ToString("0.00000000", CultureInfo.InvariantCulture) +
                      " ALT " + value.End.ElevationMeters.ToString("0.00", CultureInfo.InvariantCulture) + "M"
                    : "B NOT MARKED";
                return "A/B ABSOLUTE GEO — " + a + " | " + b;
            }
            return "A/B ABSOLUTE GEO — A NOT MARKED | B NOT MARKED";
        }

        AERISRunwayWitness FindOrCreateCalibration(AERISAirfieldDefinition airfield,
            out bool created)
        {
            for (int i = 0; i < user.Count; i++)
            {
                AERISRunwayWitness value = user[i];
                if (!MatchesAirfieldIdentity(value, airfield)) continue;
                created = false;
                return value;
            }
            var valueCreated = new AERISRunwayWitness
            {
                Body = airfield.Body ?? string.Empty,
                Name = airfield.DisplayName ?? "USER RUNWAY",
                Source = "USER_CALIBRATED",
                SourcePath = ResolvePath(CalibrationRelativePath),
                ProviderStableRecordId = airfield.ProviderStableRecordId ?? string.Empty,
                ProviderUuid = airfield.ProviderUuid ?? string.Empty,
                ProviderSiteId = airfield.ProviderSiteId ?? string.Empty,
                ProviderSourcePath = airfield.SourcePath ?? string.Empty,
                UserCalibrated = true,
                Confidence = 1.0
            };
            user.Add(valueCreated);
            created = true;
            return valueCreated;
        }

        static bool MatchesAirfieldIdentity(AERISRunwayWitness value,
            AERISAirfieldDefinition airfield)
        {
            if (value == null || airfield == null) return false;
            if (!string.IsNullOrEmpty(airfield.ProviderStableRecordId) &&
                string.Equals(value.ProviderStableRecordId,
                    airfield.ProviderStableRecordId,
                    StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(value.Body, airfield.Body,
                StringComparison.OrdinalIgnoreCase)) return false;
            return (!string.IsNullOrEmpty(airfield.ProviderUuid) &&
                    string.Equals(value.ProviderUuid, airfield.ProviderUuid,
                        StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(airfield.ProviderSiteId) &&
                    string.Equals(value.ProviderSiteId, airfield.ProviderSiteId,
                        StringComparison.OrdinalIgnoreCase));
        }

        void RollbackCalibration(AERISRunwayWitness calibration, bool created,
            AERISRunwayWitness backup)
        {
            if (created)
            {
                user.Remove(calibration);
                return;
            }
            int index = user.IndexOf(calibration);
            if (index >= 0 && backup != null) user[index] = backup;
        }

        void LoadKramaxPlans()
        {
            string directory = ResolvePath(KramaxRelativeDirectory);
            if (!Directory.Exists(directory)) return;
            string[] files;
            try { files = Directory.GetFiles(directory, "*.cfg", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                AERISLogger.Warn("[RUNWAY_WITNESS] Kramax scan failed: " + ex.Message);
                return;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                if (!string.Equals(name, "DefaultFlightPlans.cfg",
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "FlightPlans.cfg",
                    StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    ConfigNode root = ConfigNode.Load(files[i]);
                    if (root != null) ParseKramaxNode(root, files[i], dedupe);
                }
                catch (Exception ex)
                {
                    AERISLogger.Warn("[RUNWAY_WITNESS] Kramax parse failed: " +
                        files[i] + "; " + ex.Message);
                }
            }
        }

        void ParseKramaxNode(ConfigNode node, string path, HashSet<string> dedupe)
        {
            if (node == null) return;
            if (string.Equals(node.name, "FlightPlan", StringComparison.OrdinalIgnoreCase))
            {
                AERISRunwayWitness value = ParseKramaxPlan(node, path);
                if (value != null && value.IsUsable && dedupe.Add(value.Fingerprint))
                    kramax.Add(value);
            }
            foreach (ConfigNode child in node.nodes) ParseKramaxNode(child, path, dedupe);
        }

        static AERISRunwayWitness ParseKramaxPlan(ConfigNode node, string path)
        {
            ConfigNode waypoints = node.GetNode("WayPoints") ?? node.GetNode("Waypoints");
            if (waypoints == null) return null;
            ConfigNode[] points = waypoints.GetNodes("WayPoint");
            if (points == null || points.Length == 0) points = waypoints.GetNodes("Waypoint");
            if (points == null) return null;
            AERISGeoPoint rw = null;
            AERISGeoPoint stop = null;
            for (int i = 0; i < points.Length; i++)
            {
                double lat, lon, alt;
                if (!ReadDouble(points[i], "lat", out lat) ||
                    !ReadDouble(points[i], "lon", out lon)) continue;
                if (!ReadDouble(points[i], "alt", out alt)) alt = 0.0;
                var point = new AERISGeoPoint
                {
                    LatitudeDeg = Math.Max(-90.0, Math.Min(90.0, lat)),
                    LongitudeDeg = NormalizeLongitude(lon),
                    ElevationMeters = alt
                };
                if (ReadBool(points[i], "RW", false) && rw == null) rw = point;
                if (ReadBool(points[i], "Stop", false) && stop == null) stop = point;
            }
            if (rw == null || stop == null) return null;
            string body = ReadString(node, "planet", "Kerbin");
            double radius = 600000.0;
            CelestialBody bodyObject = FlightGlobals.GetBodyByName(body);
            if (bodyObject != null && bodyObject.Radius > 0.0) radius = bodyObject.Radius;
            var witness = new AERISRunwayWitness
            {
                Body = body,
                Name = ReadString(node, "name", "KRAMAX PLAN"),
                Source = "KRAMAX_PLAN",
                SourcePath = path ?? string.Empty,
                Start = rw,
                End = stop,
                HasStart = true,
                HasEnd = true,
                UserCalibrated = false,
                Confidence = 0.94
            };
            FinalizeWitness(witness, radius);
            return witness;
        }

        void LoadUserCalibrations()
        {
            string userPath = ResolvePath(CalibrationRelativePath);
            string defaultPath = ResolvePath(DefaultCalibrationRelativePath);
            string path = userPath;

            // CP3 Gate 5 Candidate 6: the completed field registration set is the
            // shipped manual-authority baseline. Seed it only when no per-install
            // calibration file exists; an existing user file always wins and is
            // never overwritten by package defaults.
            if (!File.Exists(userPath) && File.Exists(defaultPath))
            {
                try
                {
                    string directory = Path.GetDirectoryName(userPath);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.Copy(defaultPath, userPath, false);
                    AERISLogger.Info("[RUNWAY_CALIBRATION] seeded field-verified default baseline; " +
                        "source=" + defaultPath + "; destination=" + userPath + ".");
                }
                catch (Exception seedError)
                {
                    path = defaultPath;
                    AERISLogger.Warn("[RUNWAY_CALIBRATION] default baseline seed copy failed; " +
                        "loading shipped baseline read-only for this session: " +
                        seedError.Message);
                }
            }
            if (!File.Exists(path))
            {
                if (File.Exists(userPath)) path = userPath;
                else if (File.Exists(defaultPath)) path = defaultPath;
                else return;
            }
            try
            {
                ConfigNode loaded = ConfigNode.Load(path);
                ConfigNode root = ResolveCalibrationRoot(loaded);
                if (root == null)
                {
                    AERISLogger.Warn("[RUNWAY_CALIBRATION] ignored invalid root node: " + path);
                    return;
                }
                int schema;
                if (!ReadInt(root, "schema", out schema) || schema < 1 ||
                    schema > CalibrationSchema)
                {
                    AERISLogger.Warn("[RUNWAY_CALIBRATION] ignored unsupported schema: " +
                        ReadString(root, "schema", "MISSING"));
                    return;
                }
                int detectorRevision;
                if (ReadInt(root, "detectorRevision", out detectorRevision) &&
                    detectorRevision > AERISRunwaySurveySnapshot.CurrentRunwayDetectorRevision)
                {
                    AERISLogger.Warn("[RUNWAY_CALIBRATION] ignored future detector revision " +
                        detectorRevision + ".");
                    return;
                }
                ConfigNode[] nodes = root.GetNodes("Calibration");
                var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < nodes.Length; i++)
                {
                    var value = new AERISRunwayWitness
                    {
                        Body = ReadString(nodes[i], "body", string.Empty),
                        Name = ReadString(nodes[i], "name", "USER RUNWAY"),
                        Source = "USER_CALIBRATED",
                        SourcePath = path,
                        ProviderStableRecordId = ReadString(nodes[i],
                            "providerStableRecordId", string.Empty),
                        ProviderUuid = ReadString(nodes[i], "providerUuid", string.Empty),
                        ProviderSiteId = ReadString(nodes[i], "providerSiteId", string.Empty),
                        ProviderSourcePath = ReadString(nodes[i],
                            "providerSourcePath", string.Empty),
                        UserCalibrated = true,
                        Confidence = 1.0,
                        HasStart = ReadBool(nodes[i], "hasStart", false),
                        HasEnd = ReadBool(nodes[i], "hasEnd", false),
                        PlacementMismatchObserved = ReadBool(nodes[i],
                            "placementMismatchObserved", false),
                        ObservedCrossTrackMeters = ReadDoubleValue(nodes[i],
                            "observedCrossTrackMeters", 0.0),
                        ObservedAlongTrackMeters = ReadDoubleValue(nodes[i],
                            "observedAlongTrackMeters", 0.0),
                        ObservedCorridorGateMeters = ReadDoubleValue(nodes[i],
                            "observedCorridorGateMeters", 0.0),
                        PlacementObservationDetail = ReadString(nodes[i],
                            "placementObservationDetail", string.Empty)
                    };
                    value.Start = ReadPoint(nodes[i], "start");
                    value.End = ReadPoint(nodes[i], "end");
                    if (string.IsNullOrEmpty(value.Body) ||
                        (!value.HasStart && !value.HasEnd &&
                            !value.PlacementMismatchObserved) ||
                        (value.HasStart && (value.Start == null || !value.Start.IsFinite)) ||
                        (value.HasEnd && (value.End == null || !value.End.IsFinite)))
                    {
                        AERISLogger.Warn("[RUNWAY_CALIBRATION] skipped malformed calibration #" + i);
                        continue;
                    }
                    double radius = 600000.0;
                    CelestialBody body = FlightGlobals.GetBodyByName(value.Body);
                    if (body != null && body.Radius > 0.0) radius = body.Radius;
                    FinalizeWitness(value, radius);
                    if (value.HasStart && value.HasEnd && !value.IsUsable)
                    {
                        AERISLogger.Warn("[RUNWAY_CALIBRATION] skipped unusable two-point calibration #" + i);
                        continue;
                    }
                    if (schema >= 3 && value.IsUsable)
                    {
                        bool reciprocalPair = ReadBool(nodes[i],
                            "reciprocalDirectionPair", false);
                        double directionA = ReadDoubleValue(nodes[i],
                            "directionAHeadingDeg", double.NaN);
                        double directionB = ReadDoubleValue(nodes[i],
                            "directionBHeadingDeg", double.NaN);
                        if (!reciprocalPair || !Finite(directionA) || !Finite(directionB) ||
                            HeadingDifference(value.HeadingDeg, directionA) > 0.5 ||
                            HeadingDifference(value.ReciprocalHeadingDeg, directionB) > 0.5)
                        {
                            AERISLogger.Warn("[RUNWAY_CALIBRATION] skipped invalid reciprocal direction pair #" + i);
                            continue;
                        }
                    }
                    string identity = CalibrationIdentity(value);
                    if (!identities.Add(identity))
                    {
                        AERISLogger.Warn("[RUNWAY_CALIBRATION] skipped duplicate calibration: " +
                            identity);
                        continue;
                    }
                    user.Add(value);
                }
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[RUNWAY_CALIBRATION] load failed: " + ex.Message);
            }
        }

        bool SaveUserCalibrations(out string error)
        {
            error = string.Empty;
            string path = ResolvePath(CalibrationRelativePath);
            string directory = Path.GetDirectoryName(path);
            try
            {
                Directory.CreateDirectory(directory);
                var root = new ConfigNode("AERIS_USER_RUNWAY_CALIBRATIONS");
                root.AddValue("schema", CalibrationSchema);
                root.AddValue("detectorRevision",
                    AERISRunwaySurveySnapshot.CurrentRunwayDetectorRevision);
                int serializedCount = 0;
                for (int i = 0; i < user.Count; i++)
                {
                    AERISRunwayWitness value = user[i];
                    if (value == null) continue;
                    ConfigNode node = root.AddNode("Calibration");
                    serializedCount++;
                    node.AddValue("body", value.Body ?? string.Empty);
                    node.AddValue("name", value.Name ?? string.Empty);
                    node.AddValue("providerStableRecordId",
                        value.ProviderStableRecordId ?? string.Empty);
                    node.AddValue("providerUuid", value.ProviderUuid ?? string.Empty);
                    node.AddValue("providerSiteId", value.ProviderSiteId ?? string.Empty);
                    node.AddValue("providerSourcePath",
                        value.ProviderSourcePath ?? string.Empty);
                    node.AddValue("coordinateFrame", "BODY_FIXED_GEODETIC_ABSOLUTE");
                    node.AddValue("hasStart", value.HasStart);
                    node.AddValue("hasEnd", value.HasEnd);
                    node.AddValue("reciprocalDirectionPair",
                        value.HasReciprocalDirectionPair);
                    node.AddValue("directionAHeadingDeg",
                        (value.IsUsable ? value.HeadingDeg : 0.0).ToString("R",
                            CultureInfo.InvariantCulture));
                    node.AddValue("directionBHeadingDeg",
                        (value.IsUsable ? value.ReciprocalHeadingDeg : 0.0).ToString("R",
                            CultureInfo.InvariantCulture));
                    node.AddValue("placementMismatchObserved",
                        value.PlacementMismatchObserved);
                    node.AddValue("observedCrossTrackMeters",
                        value.ObservedCrossTrackMeters.ToString("R",
                            CultureInfo.InvariantCulture));
                    node.AddValue("observedAlongTrackMeters",
                        value.ObservedAlongTrackMeters.ToString("R",
                            CultureInfo.InvariantCulture));
                    node.AddValue("observedCorridorGateMeters",
                        value.ObservedCorridorGateMeters.ToString("R",
                            CultureInfo.InvariantCulture));
                    node.AddValue("placementObservationDetail",
                        value.PlacementObservationDetail ?? string.Empty);
                    WritePoint(node, "start", value.Start);
                    WritePoint(node, "end", value.End);
                }
                string temporary = path + ".tmp";
                string backup = path + ".bak";
                if (File.Exists(temporary)) File.Delete(temporary);
                root.Save(temporary);
                ValidateCalibrationDocument(ConfigNode.Load(temporary),
                    serializedCount, "temporary");
                if (File.Exists(backup)) File.Delete(backup);
                if (File.Exists(path)) File.Move(path, backup);
                try
                {
                    File.Move(temporary, path);
                    ValidateCalibrationDocument(ConfigNode.Load(path),
                        serializedCount, "committed");
                }
                catch
                {
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(backup)) File.Move(backup, path);
                    throw;
                }
                try
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }
                catch (Exception cleanup)
                {
                    AERISLogger.Warn("[RUNWAY_CALIBRATION] backup cleanup deferred: " +
                        cleanup.Message);
                }
                AERISLogger.Info("[RUNWAY_CALIBRATION] save verified; records=" +
                    serializedCount + "; fullRoundTrip=True; committedReadback=True; " +
                    "reciprocalPairSchema=3.");
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    string temporary = path + ".tmp";
                    string backup = path + ".bak";
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (File.Exists(backup) && !File.Exists(path)) File.Move(backup, path);
                }
                catch { }
                error = "CALIBRATION SAVE FAILED: " + ex.GetType().Name + " — " + ex.Message;
                AERISLogger.Warn("[RUNWAY_CALIBRATION] " + error);
                return false;
            }
        }

        static void ValidateCalibrationDocument(ConfigNode loaded, int expectedCount,
            string phase)
        {
            ConfigNode verification = ResolveCalibrationRoot(loaded);
            int verifiedSchema;
            if (verification == null ||
                !ReadInt(verification, "schema", out verifiedSchema) ||
                verifiedSchema != CalibrationSchema)
                throw new InvalidDataException(phase +
                    " calibration file verification failed");
            ConfigNode[] verifiedRecords = verification.GetNodes("Calibration");
            if (verifiedRecords.Length != expectedCount)
                throw new InvalidDataException(phase +
                    " calibration record count mismatch");
            for (int i = 0; i < verifiedRecords.Length; i++)
            {
                string body = ReadString(verifiedRecords[i], "body", string.Empty);
                bool hasStart = ReadBool(verifiedRecords[i], "hasStart", false);
                bool hasEnd = ReadBool(verifiedRecords[i], "hasEnd", false);
                bool mismatch = ReadBool(verifiedRecords[i],
                    "placementMismatchObserved", false);
                if (string.IsNullOrEmpty(body) || (!hasStart && !hasEnd && !mismatch))
                    throw new InvalidDataException(phase +
                        " calibration record validation failed at index " + i);
                if (hasStart && hasEnd)
                {
                    bool reciprocalPair = ReadBool(verifiedRecords[i],
                        "reciprocalDirectionPair", false);
                    double directionA = ReadDoubleValue(verifiedRecords[i],
                        "directionAHeadingDeg", double.NaN);
                    double directionB = ReadDoubleValue(verifiedRecords[i],
                        "directionBHeadingDeg", double.NaN);
                    if (!reciprocalPair || !Finite(directionA) || !Finite(directionB) ||
                        HeadingDifference(directionA, directionB) < 179.5)
                        throw new InvalidDataException(phase +
                            " reciprocal direction pair validation failed at index " + i);
                }
            }
        }

        // ConfigNode.Save may round-trip a named node as the loaded root itself,
        // as a named child of a generic file root, or as direct values/nodes on
        // that generic root depending on the KSP/Mono implementation.  Use the
        // same three-shape resolver as the certified airfield cache so manual
        // runway calibration is durable on native Linux Mono as well.
        static ConfigNode ResolveCalibrationRoot(ConfigNode loaded)
        {
            if (loaded == null) return null;
            const string name = "AERIS_USER_RUNWAY_CALIBRATIONS";
            if (string.Equals(loaded.name, name,
                StringComparison.OrdinalIgnoreCase)) return loaded;
            ConfigNode named = loaded.GetNode(name);
            if (named != null) return named;
            string schema = loaded.GetValue("schema");
            return string.IsNullOrEmpty(schema) ? null : loaded;
        }

        static void FinalizeWitness(AERISRunwayWitness value, double radius)
        {
            if (value == null || !value.HasStart || !value.HasEnd ||
                value.Start == null || value.End == null ||
                !value.Start.IsFinite || !value.End.IsFinite) return;
            value.LengthMeters = AERISAirfieldConfigParser.GreatCircleDistanceMeters(
                value.Start, value.End, Math.Max(1.0, radius));
            value.HeadingDeg = InitialBearing(value.Start, value.End);
            value.Fingerprint = Sha256Hex((value.Source ?? string.Empty) + "\n" +
                (value.Body ?? string.Empty) + "\n" + (value.Name ?? string.Empty) + "\n" +
                value.Start.LatitudeDeg.ToString("R", CultureInfo.InvariantCulture) + "\n" +
                value.Start.LongitudeDeg.ToString("R", CultureInfo.InvariantCulture) + "\n" +
                value.End.LatitudeDeg.ToString("R", CultureInfo.InvariantCulture) + "\n" +
                value.End.LongitudeDeg.ToString("R", CultureInfo.InvariantCulture));
        }

        static string CalibrationIdentity(AERISRunwayWitness value)
        {
            if (value == null) return "NULL";
            if (!string.IsNullOrEmpty(value.ProviderStableRecordId))
                return "STABLE|" + value.ProviderStableRecordId;
            if (!string.IsNullOrEmpty(value.ProviderUuid))
                return "UUID|" + value.Body + "|" + value.ProviderUuid;
            return "SITE|" + value.Body + "|" + value.ProviderSiteId + "|" +
                value.ProviderSourcePath;
        }

        static AERISGeoPoint Midpoint(AERISGeoPoint a, AERISGeoPoint b)
        {
            return new AERISGeoPoint
            {
                LatitudeDeg = (a.LatitudeDeg + b.LatitudeDeg) * 0.5,
                LongitudeDeg = NormalizeLongitude(a.LongitudeDeg +
                    LongitudeDelta(a.LongitudeDeg, b.LongitudeDeg) * 0.5),
                ElevationMeters = (a.ElevationMeters + b.ElevationMeters) * 0.5
            };
        }

        static double InitialBearing(AERISGeoPoint a, AERISGeoPoint b)
        {
            double lat1 = a.LatitudeDeg * Math.PI / 180.0;
            double lat2 = b.LatitudeDeg * Math.PI / 180.0;
            double lon = LongitudeDelta(a.LongitudeDeg, b.LongitudeDeg) * Math.PI / 180.0;
            double y = Math.Sin(lon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(lon);
            return AERISAirfieldConfigParser.NormalizeHeading(Math.Atan2(y, x) * 180.0 / Math.PI);
        }

        static double HeadingDifference(double a, double b)
        {
            double delta = AERISAirfieldConfigParser.NormalizeHeading(a) -
                AERISAirfieldConfigParser.NormalizeHeading(b);
            delta %= 360.0;
            if (delta > 180.0) delta -= 360.0;
            if (delta < -180.0) delta += 360.0;
            return Math.Abs(delta);
        }

        static string RunwayPairLabel(AERISRunwayWitness value)
        {
            return value == null || !value.IsUsable ? "RWY --/--" :
                "RWY " + RunwayNumber(value.HeadingDeg) + "/" +
                RunwayNumber(value.ReciprocalHeadingDeg);
        }

        static string RunwayNumber(double heading)
        {
            int number = (int)Math.Floor((
                AERISAirfieldConfigParser.NormalizeHeading(heading) + 5.0) / 10.0) % 36;
            if (number <= 0) number = 36;
            return number.ToString("00", CultureInfo.InvariantCulture);
        }

        static double NameAffinity(string a, string b)
        {
            var left = Tokens(a);
            var right = Tokens(b);
            if (left.Count == 0 || right.Count == 0) return 0.0;
            int common = 0;
            foreach (string token in left) if (right.Contains(token)) common++;
            return common / (double)Math.Max(left.Count, right.Count);
        }

        static HashSet<string> Tokens(string value)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = (value ?? string.Empty).ToUpperInvariant().Split(
                new[] { ' ', '-', '_', '/', '\\', '.', '\'', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length >= 3 && parts[i] != "RUNWAY" && parts[i] != "RWY" &&
                    parts[i] != "AIRFIELD" && parts[i] != "ILS") result.Add(parts[i]);
            return result;
        }

        static AERISGeoPoint ReadPoint(ConfigNode node, string prefix)
        {
            double lat, lon, alt;
            ReadDouble(node, prefix + "Lat", out lat);
            ReadDouble(node, prefix + "Lon", out lon);
            ReadDouble(node, prefix + "Alt", out alt);
            return new AERISGeoPoint
            {
                LatitudeDeg = lat,
                LongitudeDeg = NormalizeLongitude(lon),
                ElevationMeters = alt
            };
        }

        static void WritePoint(ConfigNode node, string prefix, AERISGeoPoint point)
        {
            point = point ?? new AERISGeoPoint();
            node.AddValue(prefix + "Lat", point.LatitudeDeg.ToString("R",
                CultureInfo.InvariantCulture));
            node.AddValue(prefix + "Lon", point.LongitudeDeg.ToString("R",
                CultureInfo.InvariantCulture));
            node.AddValue(prefix + "Alt", point.ElevationMeters.ToString("R",
                CultureInfo.InvariantCulture));
        }

        static double BodyRadius(CelestialBody body)
        {
            return body == null || body.Radius <= 0.0 ? 600000.0 : body.Radius;
        }

        static string ResolvePath(string relative)
        {
            return Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath,
                relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        static string ReadString(ConfigNode node, string key, string fallback)
        {
            return node != null && node.HasValue(key) ? node.GetValue(key) ?? fallback : fallback;
        }

        static double ReadDoubleValue(ConfigNode node, string key,
            double fallback)
        {
            if (node == null || !node.HasValue(key)) return fallback;
            double value;
            return double.TryParse(node.GetValue(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        static bool ReadInt(ConfigNode node, string key, out int value)
        {
            value = 0;
            return node != null && node.HasValue(key) &&
                int.TryParse(node.GetValue(key), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value);
        }

        static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            bool value;
            return node != null && node.HasValue(key) &&
                bool.TryParse(node.GetValue(key), out value) ? value : fallback;
        }

        static bool ReadDouble(ConfigNode node, string key, out double value)
        {
            value = 0.0;
            return node != null && node.HasValue(key) &&
                double.TryParse(node.GetValue(key), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) && Finite(value);
        }

        static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        static double LongitudeDelta(double from, double to)
        {
            return NormalizeLongitude(to - from);
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
