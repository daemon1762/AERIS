using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISProviderAlias
    {
        internal AERISAirfieldSource Source;
        internal string ProviderSiteId = string.Empty;
        internal string ProviderGroup = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderVersion = string.Empty;
        internal string SourceMod = string.Empty;
        internal string SourcePath = string.Empty;
        internal string ModelName = string.Empty;
        internal string DisplayName = string.Empty;
        internal string LegacyStableRecordId = string.Empty;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double ElevationMeters;
        internal double HeadingDeg;
        internal double DeclaredLengthMeters;
        internal double DeclaredWidthMeters;
        internal bool ReferencePositionValid;

        internal static AERISProviderAlias FromRecord(AERISProviderFacilityRecord record)
        {
            if (record == null) return null;
            return new AERISProviderAlias
            {
                Source = record.Source,
                ProviderSiteId = record.ProviderSiteId ?? string.Empty,
                ProviderGroup = record.ProviderGroup ?? string.Empty,
                ProviderUuid = record.ProviderUuid ?? string.Empty,
                ProviderVersion = record.ProviderVersion ?? string.Empty,
                SourceMod = record.SourceMod ?? string.Empty,
                SourcePath = NormalizePath(record.SourcePath),
                ModelName = record.ModelName ?? string.Empty,
                DisplayName = record.DisplayName ?? string.Empty,
                LegacyStableRecordId = AERISProviderIdentity.LegacyStableRecordId(record),
                LatitudeDeg = record.LatitudeDeg,
                LongitudeDeg = record.LongitudeDeg,
                ElevationMeters = record.ElevationMeters,
                HeadingDeg = record.OrientationHeadingDeg,
                DeclaredLengthMeters = record.DeclaredLengthMeters,
                DeclaredWidthMeters = record.DeclaredWidthMeters,
                ReferencePositionValid = record.ProviderReferencePositionValid
            };
        }

        static string NormalizePath(string value)
        {
            return (value ?? string.Empty).Trim().Replace('\\', '/');
        }
    }

    internal sealed class AERISPhysicalRunwayMergeSummary
    {
        internal int RawProviderRecords;
        internal int RawRunwayRecords;
        internal int CanonicalProviderRecords;
        internal int CanonicalRunwayRecords;
        internal int MergedAliasRecords;
        internal int MultiProviderRunways;
        internal string IdentitySignature = string.Empty;

        internal string StatusText
        {
            get
            {
                return "RAW " + RawProviderRecords + " / " + RawRunwayRecords +
                    " RWY; CANONICAL " + CanonicalProviderRecords + " / " +
                    CanonicalRunwayRecords + " RWY; MERGED " + MergedAliasRecords +
                    " ALIAS(ES); MULTI-PROVIDER " + MultiProviderRunways;
            }
        }
    }

    // Conservative provider federation.  It does not infer runway geometry and does
    // not certify an approach.  Its sole job is to ensure that one physical strip is
    // represented by one survey/cache authority while preserving every source record
    // as an auditable alias.
    internal static class AERISPhysicalRunwayIdentity
    {
        const double DefaultBodyRadiusMeters = 600000.0;
        const double MaximumAxisDifferenceDeg = 8.0;
        const double MaximumUnknownLengthMergeMeters = 650.0;

        sealed class Cluster
        {
            internal readonly List<AERISProviderFacilityRecord> Members =
                new List<AERISProviderFacilityRecord>();
            internal AERISProviderFacilityRecord Preferred;
            internal string BaseIdentity = string.Empty;
            internal string PhysicalId = string.Empty;
            internal double CentroidLatitude;
            internal double CentroidLongitude;
            internal bool CentroidValid;
        }

        internal static List<AERISProviderFacilityRecord> Canonicalize(
            IList<AERISProviderFacilityRecord> source,
            out AERISPhysicalRunwayMergeSummary summary)
        {
            summary = new AERISPhysicalRunwayMergeSummary();
            var nonRunways = new List<AERISProviderFacilityRecord>();
            var runways = new List<AERISProviderFacilityRecord>();
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    AERISProviderFacilityRecord record = source[i];
                    if (record == null) continue;
                    summary.RawProviderRecords++;
                    if (record.FacilityKind == AERISFacilityKind.Runway)
                    {
                        summary.RawRunwayRecords++;
                        runways.Add(record);
                    }
                    else nonRunways.Add(record);
                }

            runways.Sort(CompareProviderRecords);
            // Complete-link clustering avoids transitive bridge merges.  A provider
            // record may join a physical runway only when it is compatible with every
            // member already in that cluster, not merely with one intermediate alias.
            var clusters = new List<Cluster>();
            for (int i = 0; i < runways.Count; i++)
            {
                AERISProviderFacilityRecord candidate = runways[i];
                Cluster destination = null;
                for (int j = 0; j < clusters.Count; j++)
                {
                    bool compatible = true;
                    for (int k = 0; k < clusters[j].Members.Count; k++)
                        if (!RepresentsSamePhysicalRunway(candidate,
                            clusters[j].Members[k]))
                        {
                            compatible = false;
                            break;
                        }
                    if (compatible)
                    {
                        destination = clusters[j];
                        break;
                    }
                }
                if (destination == null)
                {
                    destination = new Cluster();
                    clusters.Add(destination);
                }
                destination.Members.Add(candidate);
            }

            for (int i = 0; i < clusters.Count; i++) PrepareCluster(clusters[i]);
            // Dictionary root enumeration is not a deterministic identity source.  Sort
            // the fully prepared clusters before assigning collision suffixes so the
            // same provider set produces the same physical runway IDs on every KSP run.
            clusters.Sort(CompareClustersForIdentity);
            AssignStablePhysicalIds(clusters);
            clusters.Sort(delegate(Cluster a, Cluster b)
            {
                return string.Compare(a.PhysicalId, b.PhysicalId,
                    StringComparison.OrdinalIgnoreCase);
            });

            var result = new List<AERISProviderFacilityRecord>(
                nonRunways.Count + clusters.Count);
            nonRunways.Sort(CompareProviderRecords);
            for (int i = 0; i < nonRunways.Count; i++) result.Add(nonRunways[i]);
            for (int i = 0; i < clusters.Count; i++)
            {
                Cluster cluster = clusters[i];
                AERISProviderFacilityRecord canonical = cluster.Preferred;
                canonical.PhysicalRunwayId = cluster.PhysicalId;
                canonical.IsCanonicalPhysicalRunway = true;
                canonical.ProviderAliases = new List<AERISProviderAlias>();
                canonical.CanonicalProviderReason = BuildCanonicalReason(cluster);
                MergeStableMetadata(canonical, cluster.Members);
                MergeRuntimeGeometry(canonical, cluster.Members);
                for (int j = 0; j < cluster.Members.Count; j++)
                {
                    AERISProviderAlias alias = AERISProviderAlias.FromRecord(
                        cluster.Members[j]);
                    if (alias != null) canonical.ProviderAliases.Add(alias);
                }
                canonical.ProviderAliases.Sort(CompareAliases);
                result.Add(canonical);
                if (cluster.Members.Count > 1)
                {
                    summary.MultiProviderRunways++;
                    summary.MergedAliasRecords += cluster.Members.Count - 1;
                }
            }
            summary.CanonicalProviderRecords = result.Count;
            summary.CanonicalRunwayRecords = clusters.Count;
            summary.IdentitySignature = HashPhysicalIdentities(clusters);
            return result;
        }

        internal static bool RepresentsSamePhysicalRunway(
            AERISProviderFacilityRecord a, AERISProviderFacilityRecord b)
        {
            if (a == null || b == null ||
                a.FacilityKind != AERISFacilityKind.Runway ||
                b.FacilityKind != AERISFacilityKind.Runway) return false;
            if (!string.Equals(NormalizeBody(a.Body), NormalizeBody(b.Body),
                StringComparison.OrdinalIgnoreCase)) return false;

            string aName = PhysicalSiteToken(a);
            string bName = PhysicalSiteToken(b);
            if (string.IsNullOrEmpty(aName) || string.IsNullOrEmpty(bName) ||
                !string.Equals(aName, bName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!RunwayNumberHintsCompatible(a, b)) return false;

            bool aHeading = FiniteHeading(a.OrientationHeadingDeg);
            bool bHeading = FiniteHeading(b.OrientationHeadingDeg);
            if (aHeading && bHeading && AxisDelta(a.OrientationHeadingDeg,
                b.OrientationHeadingDeg) > MaximumAxisDifferenceDeg) return false;

            if (a.ProviderReferencePositionValid && b.ProviderReferencePositionValid)
            {
                double distance = HorizontalDistanceMeters(a.LatitudeDeg, a.LongitudeDeg,
                    b.LatitudeDeg, b.LongitudeDeg, ResolveBodyRadiusMeters(a, b));
                double knownLength = Math.Max(Positive(a.DeclaredLengthMeters),
                    Positive(b.DeclaredLengthMeters));
                double allowed = knownLength > 0.0
                    ? Math.Min(3500.0, Math.Max(450.0, knownLength * 0.72 + 120.0))
                    : MaximumUnknownLengthMergeMeters;
                if (!Finite(distance) || distance > allowed) return false;
            }
            else
            {
                // With no geodetic positions, require an additional stable source hint.
                bool sameModel = StableEquals(a.ModelName, b.ModelName);
                bool sameGroup = StableEquals(NormalizeName(a.ProviderGroup, false),
                    NormalizeName(b.ProviderGroup, false));
                if (!sameModel && !sameGroup) return false;
            }
            return true;
        }

        internal static string PhysicalSiteToken(AERISProviderFacilityRecord record)
        {
            if (record == null) return string.Empty;
            string site = FirstNonEmpty(record.ProviderSiteId, record.DisplayName,
                record.ProviderGroup);
            return NormalizeName(site, true);
        }

        static void PrepareCluster(Cluster cluster)
        {
            cluster.Members.Sort(CompareProviderRecords);
            cluster.Preferred = cluster.Members[0];
            int preferredScore = ProviderScore(cluster.Preferred);
            double lat = 0.0;
            double lonX = 0.0;
            double lonY = 0.0;
            int positionCount = 0;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                AERISProviderFacilityRecord candidate = cluster.Members[i];
                int score = ProviderScore(candidate);
                if (score > preferredScore || (score == preferredScore &&
                    CompareProviderRecords(candidate, cluster.Preferred) < 0))
                {
                    preferredScore = score;
                    cluster.Preferred = candidate;
                }
                if (candidate.ProviderReferencePositionValid &&
                    Finite(candidate.LatitudeDeg) && Finite(candidate.LongitudeDeg))
                {
                    lat += candidate.LatitudeDeg;
                    double radians = candidate.LongitudeDeg * Math.PI / 180.0;
                    lonX += Math.Cos(radians);
                    lonY += Math.Sin(radians);
                    positionCount++;
                }
            }
            if (positionCount > 0)
            {
                cluster.CentroidLatitude = lat / positionCount;
                cluster.CentroidLongitude = Math.Atan2(lonY, lonX) * 180.0 / Math.PI;
                cluster.CentroidValid = true;
            }
            string body = NormalizeBody(cluster.Preferred.Body);
            string site = PhysicalSiteToken(cluster.Preferred);
            string pair = ResolveClusterRunwayPair(cluster);
            string axis = ResolveClusterAxis(cluster);
            cluster.BaseIdentity = body + "|" + site + "|PAIR=" + pair +
                "|AXIS=" + axis;
        }

        static void AssignStablePhysicalIds(List<Cluster> clusters)
        {
            var baseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < clusters.Count; i++)
            {
                int value;
                baseCounts.TryGetValue(clusters[i].BaseIdentity, out value);
                baseCounts[clusters[i].BaseIdentity] = value + 1;
            }
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < clusters.Count; i++)
            {
                Cluster cluster = clusters[i];
                string identity = cluster.BaseIdentity;
                int count;
                if (baseCounts.TryGetValue(identity, out count) && count > 1)
                    identity += "|CELL=" + CoarseCell(cluster) +
                        "|ANCHOR=" + StableClusterDisambiguator(cluster);
                string id = "PRWY_" + StableHash(identity);
                int suffix = 2;
                string unique = id;
                while (!used.Add(unique)) unique = id + "_" + suffix++;
                cluster.PhysicalId = unique;
            }
        }


        static int CompareClustersForIdentity(Cluster a, Cluster b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int value = string.Compare(a.BaseIdentity, b.BaseIdentity,
                StringComparison.OrdinalIgnoreCase);
            if (value != 0) return value;
            value = string.Compare(CoarseCell(a), CoarseCell(b),
                StringComparison.OrdinalIgnoreCase);
            if (value != 0) return value;
            value = string.Compare(StableClusterDisambiguator(a),
                StableClusterDisambiguator(b), StringComparison.OrdinalIgnoreCase);
            if (value != 0) return value;
            return string.Compare(ClusterMemberSignature(a), ClusterMemberSignature(b),
                StringComparison.OrdinalIgnoreCase);
        }

        static string StableClusterDisambiguator(Cluster cluster)
        {
            if (cluster == null || cluster.Preferred == null) return "NONE";
            AERISProviderFacilityRecord value = cluster.Preferred;
            string sourcePath = (value.SourcePath ?? string.Empty).Trim().Replace('\\', '/');
            string sourceAnchor = value.Source.ToString().ToUpperInvariant() + "|" +
                NormalizeName(value.SourceMod, false) + "|" +
                sourcePath.ToUpperInvariant() + "|" +
                NormalizeName(value.ModelName, false) + "|" +
                NormalizeName(value.ProviderSiteId, false);
            // Fine cell is used only when two distinct clusters already share the same
            // body/site/runway-pair/axis and coarse cell.  It never participates in the
            // normal one-runway identity, so sub-metre provider-frame noise cannot churn
            // ordinary cache keys.
            string fineCell = FineCell(cluster);
            return StableHash(sourceAnchor + "|FINE=" + fineCell);
        }

        static string ClusterMemberSignature(Cluster cluster)
        {
            var values = new List<string>();
            if (cluster != null)
                for (int i = 0; i < cluster.Members.Count; i++)
                    values.Add(LegacyIdentity(cluster.Members[i]));
            values.Sort(StringComparer.OrdinalIgnoreCase);
            return StableHash(string.Join("\n", values.ToArray()));
        }

        static string ResolveClusterRunwayPair(Cluster cluster)
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cluster != null)
                for (int i = 0; i < cluster.Members.Count; i++)
                {
                    List<int> hints = ExtractRunwayNumberHints(cluster.Members[i]);
                    for (int j = 0; j < hints.Count; j++)
                        values.Add(CanonicalRunwayPairToken(hints[j]));
                }
            if (values.Count == 0) return "NA";
            var ordered = new List<string>(values);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("+", ordered.ToArray());
        }

        static string CanonicalRunwayPairToken(int runwayNumber)
        {
            int reciprocal = runwayNumber <= 18
                ? runwayNumber + 18 : runwayNumber - 18;
            int first = Math.Min(runwayNumber, reciprocal);
            int second = Math.Max(runwayNumber, reciprocal);
            return first.ToString("00", CultureInfo.InvariantCulture) + "-" +
                second.ToString("00", CultureInfo.InvariantCulture);
        }

        static string ResolveClusterAxis(Cluster cluster)
        {
            double sumX = 0.0;
            double sumY = 0.0;
            int count = 0;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                double heading = cluster.Members[i].OrientationHeadingDeg;
                if (!FiniteHeading(heading)) continue;
                double axis = NormalizeAxis(heading) * Math.PI / 180.0 * 2.0;
                sumX += Math.Cos(axis);
                sumY += Math.Sin(axis);
                count++;
            }
            if (count == 0) return "NA";
            double resolved = Math.Atan2(sumY, sumX) * 180.0 / Math.PI / 2.0;
            if (resolved < 0.0) resolved += 180.0;
            int quantized = (int)Math.Round(resolved, MidpointRounding.AwayFromZero);
            if (quantized >= 180) quantized -= 180;
            return quantized.ToString("000", CultureInfo.InvariantCulture);
        }

        static string CoarseCell(Cluster cluster)
        {
            if (cluster == null || !cluster.CentroidValid)
                return StableHash((cluster == null ? string.Empty : cluster.BaseIdentity) +
                    "|" + StableClusterDisambiguator(cluster));
            // Kerbin has about 10.5 km/degree.  0.01 degree is deliberately coarse:
            // it distinguishes parallel/same-name facilities while ignoring sub-metre
            // and threshold-vs-centre provider frame differences inside one cluster.
            return QuantizedCell(cluster.CentroidLatitude,
                cluster.CentroidLongitude, 0.01);
        }

        static string FineCell(Cluster cluster)
        {
            if (cluster == null || !cluster.CentroidValid) return "NA";
            return QuantizedCell(cluster.CentroidLatitude,
                cluster.CentroidLongitude, 0.001);
        }

        static string QuantizedCell(double latitude, double longitude, double stepDeg)
        {
            long lat = (long)Math.Round(latitude / stepDeg,
                MidpointRounding.AwayFromZero);
            long lon = (long)Math.Round(longitude / stepDeg,
                MidpointRounding.AwayFromZero);
            return lat.ToString(CultureInfo.InvariantCulture) + ":" +
                lon.ToString(CultureInfo.InvariantCulture);
        }

        static void MergeStableMetadata(AERISProviderFacilityRecord canonical,
            IList<AERISProviderFacilityRecord> members)
        {
            if (canonical == null || members == null) return;
            double length = Positive(canonical.DeclaredLengthMeters);
            double width = Positive(canonical.DeclaredWidthMeters);
            for (int i = 0; i < members.Count; i++)
            {
                AERISProviderFacilityRecord member = members[i];
                if (member == null) continue;
                length = Math.Max(length, Positive(member.DeclaredLengthMeters));
                width = Math.Max(width, Positive(member.DeclaredWidthMeters));
                if (string.IsNullOrEmpty(canonical.ProviderVersion) &&
                    !string.IsNullOrEmpty(member.ProviderVersion))
                    canonical.ProviderVersion = member.ProviderVersion;
                if (string.IsNullOrEmpty(canonical.SourceMod) &&
                    !string.IsNullOrEmpty(member.SourceMod))
                    canonical.SourceMod = member.SourceMod;
                if (string.IsNullOrEmpty(canonical.Description) &&
                    !string.IsNullOrEmpty(member.Description))
                    canonical.Description = member.Description;
                if (canonical.SurveyDefinition == null &&
                    member.SurveyDefinition != null)
                    canonical.SurveyDefinition = member.SurveyDefinition;
            }
            canonical.DeclaredLengthMeters = length;
            canonical.DeclaredWidthMeters = width;
        }

        static string BuildCanonicalReason(Cluster cluster)
        {
            if (cluster == null || cluster.Preferred == null) return "NO CANONICAL PROVIDER";
            return "PREFERRED " + cluster.Preferred.Source.ToString().ToUpperInvariant() +
                "; SCORE " + ProviderScore(cluster.Preferred) + "; " +
                cluster.Members.Count + " PROVIDER RECORD(S) FEDERATED";
        }

        static int ProviderScore(AERISProviderFacilityRecord record)
        {
            if (record == null) return int.MinValue;
            // Canonical authority must not switch merely because a live Unity object
            // or LOD happens to exist in one scene.  Stable source-authored metadata
            // dominates; runtime capture availability is only a small tie-breaker.
            int score = 0;
            switch (record.Source)
            {
                case AERISAirfieldSource.UserCfg: score += 1000; break;
                case AERISAirfieldSource.StockLaunchsitesExpansion: score += 900; break;
                case AERISAirfieldSource.KerbalKonstructs: score += 850; break;
                case AERISAirfieldSource.Dlc: score += 700; break;
                case AERISAirfieldSource.Stock: score += 650; break;
            }
            if (!string.IsNullOrEmpty(record.SourcePath)) score += 220;
            if (!string.IsNullOrEmpty(record.ModelName)) score += 180;
            if (!string.IsNullOrEmpty(record.ProviderSiteId)) score += 100;
            if (!string.IsNullOrEmpty(record.ProviderGroup)) score += 40;
            if (record.SurveyDefinition != null) score += 90;
            if (record.ProviderReferencePositionValid) score += 50;
            if (Positive(record.DeclaredLengthMeters) > 0.0) score += 25;
            if (Positive(record.DeclaredWidthMeters) > 0.0) score += 15;
            score += Math.Min(40, RuntimeGeometryScore(record));
            return score;
        }

        static int RuntimeGeometryScore(AERISProviderFacilityRecord record)
        {
            if (record == null) return int.MinValue;
            int score = 0;
            if (record.RuntimeRunwayPrefab != null) score += 80;
            if (record.RuntimeRunwayObject != null) score += 60;
            if (record.RuntimePrefabLaunchTransform != null) score += 25;
            if (record.RuntimeInstanceTransform != null) score += 20;
            if (record.RuntimeLaunchTransform != null) score += 15;
            if (record.RuntimeLaunchFrameValid) score += 10;
            if (record.RuntimeBody != null) score += 5;
            return score;
        }

        static void MergeRuntimeGeometry(AERISProviderFacilityRecord canonical,
            IList<AERISProviderFacilityRecord> members)
        {
            if (canonical == null || members == null || members.Count == 0) return;
            AERISProviderFacilityRecord runtime = canonical;
            int best = RuntimeGeometryScore(runtime);
            for (int i = 0; i < members.Count; i++)
            {
                int score = RuntimeGeometryScore(members[i]);
                if (score > best)
                {
                    best = score;
                    runtime = members[i];
                }
            }
            if (runtime == null || ReferenceEquals(runtime, canonical)) return;
            canonical.RuntimeBody = runtime.RuntimeBody;
            canonical.RuntimeLaunchTransform = runtime.RuntimeLaunchTransform;
            canonical.RuntimeInstanceTransform = runtime.RuntimeInstanceTransform;
            canonical.RuntimePrefabLaunchTransform = runtime.RuntimePrefabLaunchTransform;
            canonical.RuntimeRunwayObject = runtime.RuntimeRunwayObject;
            canonical.RuntimeRunwayPrefab = runtime.RuntimeRunwayPrefab;
            canonical.RuntimeModelScale = runtime.RuntimeModelScale;
            canonical.RuntimeLaunchFrameValid = runtime.RuntimeLaunchFrameValid;
            canonical.RuntimeLaunchPosition = runtime.RuntimeLaunchPosition;
            canonical.RuntimeLaunchForward = runtime.RuntimeLaunchForward;
        }

        static int CompareProviderRecords(AERISProviderFacilityRecord a,
            AERISProviderFacilityRecord b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int value = string.Compare(NormalizeBody(a.Body), NormalizeBody(b.Body),
                StringComparison.OrdinalIgnoreCase);
            if (value != 0) return value;
            value = string.Compare(PhysicalSiteToken(a), PhysicalSiteToken(b),
                StringComparison.OrdinalIgnoreCase);
            if (value != 0) return value;
            value = b.Source.CompareTo(a.Source);
            if (value != 0) return value;
            return string.Compare(LegacyIdentity(a), LegacyIdentity(b),
                StringComparison.OrdinalIgnoreCase);
        }

        static int CompareAliases(AERISProviderAlias a, AERISProviderAlias b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int value = a.Source.CompareTo(b.Source);
            if (value != 0) return value;
            return string.Compare(a.LegacyStableRecordId, b.LegacyStableRecordId,
                StringComparison.OrdinalIgnoreCase);
        }

        static string HashPhysicalIdentities(IList<Cluster> clusters)
        {
            var values = new List<string>();
            if (clusters != null)
                for (int i = 0; i < clusters.Count; i++)
                    values.Add(clusters[i].PhysicalId + "|" +
                        clusters[i].Members.Count.ToString(CultureInfo.InvariantCulture));
            values.Sort(StringComparer.OrdinalIgnoreCase);
            return StableHash(string.Join("\n", values.ToArray()));
        }

        static string LegacyIdentity(AERISProviderFacilityRecord record)
        {
            return AERISProviderIdentity.LegacyStableRecordId(record);
        }

        static string StableHash(string value)
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            string text = value ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= (ulong)char.ToUpperInvariant(text[i]);
                hash *= prime;
            }
            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }

        static string NormalizeBody(string value)
        {
            string body = (value ?? string.Empty).Trim();
            return string.IsNullOrEmpty(body) ? "KERBIN" : body.ToUpperInvariant();
        }

        static string NormalizeName(string value, bool removeRunwayDirection)
        {
            string upper = (value ?? string.Empty).Trim().ToUpperInvariant();
            var tokens = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i <= upper.Length; i++)
            {
                char c = i < upper.Length ? upper[i] : ' ';
                if (char.IsLetterOrDigit(c)) current.Append(c);
                else if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Length = 0;
                }
            }
            bool runwayContext = false;
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i] == "RUNWAY" || tokens[i] == "RWY") runwayContext = true;
            var result = new StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                if (token == "RUNWAY" || token == "RWY" || token == "AIRFIELD" ||
                    token == "AIRPORT" || token == "LAUNCHSITE") continue;
                if (removeRunwayDirection && runwayContext && IsRunwayNumber(token)) continue;
                result.Append(token);
            }
            return result.ToString();
        }

        static bool IsRunwayNumber(string token)
        {
            int number;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out number)) return false;
            return number >= 1 && number <= 36;
        }

        static bool RunwayNumberHintsCompatible(AERISProviderFacilityRecord a,
            AERISProviderFacilityRecord b)
        {
            List<int> left = ExtractRunwayNumberHints(a);
            List<int> right = ExtractRunwayNumberHints(b);
            if (left.Count == 0 || right.Count == 0) return true;
            for (int i = 0; i < left.Count; i++)
                for (int j = 0; j < right.Count; j++)
                    if (left[i] == right[j] || Math.Abs(left[i] - right[j]) == 18)
                        return true;
            return false;
        }

        static List<int> ExtractRunwayNumberHints(AERISProviderFacilityRecord record)
        {
            var values = new HashSet<int>();
            if (record == null) return new List<int>();
            AddRunwayNumberHints(values, record.DisplayName);
            AddRunwayNumberHints(values, record.ProviderSiteId);
            AddRunwayNumberHints(values, record.ProviderGroup);
            var result = new List<int>(values);
            result.Sort();
            return result;
        }

        static void AddRunwayNumberHints(HashSet<int> values, string source)
        {
            if (values == null || string.IsNullOrEmpty(source)) return;
            string upper = source.Trim().ToUpperInvariant();
            var tokens = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i <= upper.Length; i++)
            {
                char c = i < upper.Length ? upper[i] : ' ';
                if (char.IsLetterOrDigit(c)) current.Append(c);
                else if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Length = 0;
                }
            }
            bool runwayContext = false;
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i] == "RUNWAY" || tokens[i] == "RWY")
                {
                    runwayContext = true;
                    break;
                }
            if (!runwayContext) return;
            for (int i = 0; i < tokens.Count; i++)
            {
                int number;
                if (int.TryParse(tokens[i], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out number) &&
                    number >= 1 && number <= 36) values.Add(number);
            }
        }

        static double NormalizeAxis(double heading)
        {
            double value = heading % 180.0;
            if (value < 0.0) value += 180.0;
            return value;
        }

        static double AxisDelta(double a, double b)
        {
            double delta = Math.Abs(NormalizeAxis(a) - NormalizeAxis(b));
            return delta > 90.0 ? 180.0 - delta : delta;
        }

        static bool FiniteHeading(double value)
        {
            return Finite(value) && Math.Abs(value) <= 100000.0;
        }

        static bool StableEquals(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrEmpty(values[i])) return values[i];
            return string.Empty;
        }

        static double Positive(double value)
        {
            return Finite(value) && value > 0.0 ? value : 0.0;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static double ResolveBodyRadiusMeters(AERISProviderFacilityRecord a,
            AERISProviderFacilityRecord b)
        {
            double radius = a != null && a.RuntimeBody != null
                ? a.RuntimeBody.Radius : 0.0;
            if (!Finite(radius) || radius < 1000.0)
                radius = b != null && b.RuntimeBody != null ? b.RuntimeBody.Radius : 0.0;
            return Finite(radius) && radius >= 1000.0
                ? radius : DefaultBodyRadiusMeters;
        }

        static double HorizontalDistanceMeters(double latitudeA, double longitudeA,
            double latitudeB, double longitudeB, double radius)
        {
            const double radians = Math.PI / 180.0;
            double meanLatitude = (latitudeA + latitudeB) * 0.5 * radians;
            double north = (latitudeB - latitudeA) * radians * radius;
            double longitudeDelta = longitudeB - longitudeA;
            while (longitudeDelta > 180.0) longitudeDelta -= 360.0;
            while (longitudeDelta < -180.0) longitudeDelta += 360.0;
            double east = longitudeDelta * radians * radius * Math.Cos(meanLatitude);
            return Math.Sqrt(north * north + east * east);
        }
    }
}
