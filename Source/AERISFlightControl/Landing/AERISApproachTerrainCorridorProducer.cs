using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AERISFlightControl.Terrain;
using AERISFlightControl.Performance;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISTerrainCorridorIdentity
    {
        internal string DirectionStableId = string.Empty;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal string EnvironmentSignature = string.Empty;
        internal long AirfieldDatabaseRevision;
        internal long RunwayGeometryRevision;
        internal long TerrainRequestGeneration;
        internal long TerrainDatabaseGeneration;
        internal long ProducerGeneration;
    }

    internal sealed class AERISTerrainCorridorComputeInput
    {
        internal AERISTerrainCorridorIdentity Identity;
        internal string GameDataHash = string.Empty;
        internal double ThresholdLatitudeDeg;
        internal double ThresholdLongitudeDeg;
        internal double ThresholdElevationMeters;
        internal double InboundHeadingDeg;
        internal double MissedApproachHeadingDeg;
        internal double RequiredMissedApproachAltitudeMeters;
        internal AERISApproachPlanningLimits Limits;
        internal AERISTerrainCorridorReadResult ReadResult;
    }

    // Main-thread scheduling consumes captured values; worker delegates contain only
    // the detached compute input. Runtime capabilities never enter that closure.
    internal sealed class AERISTerrainCorridorDirectionCapture
    {
        internal AERISTerrainCorridorComputeInput Input;
        internal int Priority;
    }

    internal sealed class AERISApproachTerrainCorridorProducer
    {
        const int MaximumInFlightDirections = 2;
        readonly Func<string, IList<AERISTerrainCorridorDirectionCapture>> captureDirections;
        readonly Func<AERISTerrainCorridorComputeInput, AERISTerrainCorridorReadPlan> capturePlan;
        readonly Func<AERISTerrainCorridorReadPlan, bool> isPlanCurrent;
        readonly Func<AERISTerrainCorridorReadPlan, Action<AERISTerrainCorridorReadResult>, bool> submitRead;
        readonly Func<AERISTerrainCorridorComputeInput, Action<AERISApproachObstacleSnapshot>, bool> submitCompute;
        readonly Action<string> log;
        readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Attempt> attempts = new Dictionary<string, Attempt>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Publication> published = new Dictionary<string, Publication>(StringComparer.OrdinalIgnoreCase);
        long producerGeneration;
        long publicationGeneration;
        long cycle;
        string activeBody = string.Empty;
        string lastSummary = string.Empty;

        sealed class Job
        {
            internal AERISTerrainCorridorComputeInput Input;
            internal AERISTerrainCorridorReadPlan Plan;
            internal int Phase;
            internal bool Retired;
        }
        sealed class Attempt
        {
            internal AERISTerrainCorridorComputeInput Input;
            internal int Priority;
            internal bool Pending;
        }
        sealed class Publication
        {
            internal AERISTerrainCorridorComputeInput Input;
            internal AERISTerrainCorridorReadPlan Plan;
            internal AERISApproachObstacleSnapshot Snapshot;
            internal long ValidatedDatabaseGeneration;
        }

        // These adapters run on the main thread. Keeping admission and publication
        // independent of KSP also permits deterministic copied-value lifecycle tests.
        internal AERISApproachTerrainCorridorProducer(
            Func<string, IList<AERISTerrainCorridorDirectionCapture>> captureDirections,
            Func<AERISTerrainCorridorComputeInput, AERISTerrainCorridorReadPlan> capturePlan,
            Func<AERISTerrainCorridorReadPlan, bool> isPlanCurrent,
            Func<AERISTerrainCorridorReadPlan, Action<AERISTerrainCorridorReadResult>, bool> submitRead,
            Func<AERISTerrainCorridorComputeInput, Action<AERISApproachObstacleSnapshot>, bool> submitCompute,
            Action<string> log)
        {
            this.captureDirections = captureDirections;
            this.capturePlan = capturePlan;
            this.isPlanCurrent = isPlanCurrent;
            this.submitRead = submitRead;
            this.submitCompute = submitCompute;
            this.log = log;
        }

        internal AERISApproachTerrainCorridorProducer(AERISAirfieldRegistry airfields,
            AERISLandingFoundation landing, AERISTerrainCorridorReadService reads,
            AERISPerformanceRuntime performance, AERISApproachPlanningLimits limits)
        {
            AERISApproachPlanningLimits copiedLimits = (limits ?? new AERISApproachPlanningLimits()).Clone();
            captureDirections = body => CaptureDirections(airfields, landing, reads, copiedLimits, body);
            capturePlan = input => BuildPlan(reads, input);
            isPlanCurrent = plan => reads != null && reads.IsPlanCurrent(plan);
            submitRead = delegate(AERISTerrainCorridorReadPlan plan, Action<AERISTerrainCorridorReadResult> commit)
            {
                AERISPerformanceRuntime runtime = performance ?? AERISPerformanceRuntime.Current;
                return reads != null && runtime != null && reads.SubmitRead(plan, runtime.CaptureStamp(), commit);
            };
            submitCompute = delegate(AERISTerrainCorridorComputeInput input, Action<AERISApproachObstacleSnapshot> commit)
            {
                AERISPerformanceRuntime runtime = performance ?? AERISPerformanceRuntime.Current;
                return runtime != null && runtime.Scheduler != null &&
                    runtime.Scheduler.SubmitRequired(AERISRuntimeLane.SafetyLand,
                        "land-r2-compute|" + input.Identity.DirectionStableId + "|" + input.Identity.ProducerGeneration,
                        runtime.CaptureStamp(), ComputeWork(input),
                        value => commit(value as AERISApproachObstacleSnapshot), false);
            };
            log = AERISLogger.Info;
        }

        // A separate factory prevents a compiler display class containing runtime,
        // producer, registry, or commit closures from crossing the worker boundary.
        static Func<AERISRuntimeJobContext, object> ComputeWork(AERISTerrainCorridorComputeInput input)
        {
            return context => AnalyzePure(input);
        }

        internal long PublicationGeneration { get { return publicationGeneration; } }
        internal int InFlightCount { get { return jobs.Count; } }

        internal IDictionary<string, AERISApproachObstacleSnapshot> SnapshotDictionary()
        {
            var result = new Dictionary<string, AERISApproachObstacleSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in published) result.Add(pair.Key, pair.Value.Snapshot.Clone());
            return result;
        }

        internal void Reset(string reason)
        {
            producerGeneration++;
            attempts.Clear();
            if (published.Count > 0) { published.Clear(); publicationGeneration++; }
            // Already admitted physical jobs retain their credits until terminal
            // callbacks. Their old producer generation rejects them immediately.
            Emit("RESET", null, "reason=" + reason);
            Summary();
        }

        IList<AERISTerrainCorridorDirectionCapture> Capture()
        {
            IList<AERISTerrainCorridorDirectionCapture> values = captureDirections(activeBody);
            foreach (var value in values) value.Input.Identity.ProducerGeneration = producerGeneration;
            return values;
        }

        internal void Tick(string activeBodyName)
        {
            activeBody = activeBodyName ?? string.Empty;
            IList<AERISTerrainCorridorDirectionCapture> current = Capture();
            var byId = new Dictionary<string, AERISTerrainCorridorDirectionCapture>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in current) byId[value.Input.Identity.DirectionStableId] = value;
            var revoked = new List<string>();
            foreach (var pair in published)
            {
                AERISTerrainCorridorDirectionCapture value;
                if (!byId.TryGetValue(pair.Key, out value) ||
                    !Current(pair.Value.Input, value.Input, pair.Value.Plan,
                        value.Input.Identity.TerrainDatabaseGeneration != pair.Value.ValidatedDatabaseGeneration))
                    revoked.Add(pair.Key);
                else pair.Value.ValidatedDatabaseGeneration = value.Input.Identity.TerrainDatabaseGeneration;
            }
            foreach (string id in revoked)
            {
                Emit("STALE_REJECT", published[id].Input.Identity, "reason=PUBLICATION_REVOKED");
                published.Remove(id);
                publicationGeneration++;
            }
            var queue = new List<AERISTerrainCorridorDirectionCapture>();
            foreach (var value in byId.Values)
            {
                string id = value.Input.Identity.DirectionStableId;
                if (jobs.ContainsKey(id)) continue;
                Attempt previous;
                bool promoted = attempts.TryGetValue(id, out previous) && previous.Priority >= 2 && value.Priority < 2;
                Publication existing;
                if (published.TryGetValue(id, out existing) && existing.Snapshot.TerrainCoverageComplete) continue;
                bool same = previous != null && IdentityMatches(previous.Input.Identity, value.Input.Identity) &&
                    string.Equals(previous.Input.GameDataHash, value.Input.GameDataHash, StringComparison.Ordinal);
                if (same && !promoted)
                {
                    // Remember demotion too, so a later background-to-selected
                    // transition is a new retry opportunity.
                    previous.Priority = value.Priority;
                    continue;
                }
                queue.Add(value);
            }
            queue.Sort(delegate(AERISTerrainCorridorDirectionCapture left, AERISTerrainCorridorDirectionCapture right)
            {
                int rank = left.Priority.CompareTo(right.Priority);
                return rank != 0 ? rank : StringComparer.OrdinalIgnoreCase.Compare(left.Input.Identity.DirectionStableId, right.Input.Identity.DirectionStableId);
            });
            if (queue.Count > 0) cycle++;
            for (int i = 0; i < queue.Count && jobs.Count < MaximumInFlightDirections; i++)
            {
                var value = queue[i];
                string id = value.Input.Identity.DirectionStableId;
                attempts[id] = new Attempt { Input = value.Input, Priority = value.Priority };
                AERISTerrainCorridorReadPlan plan = capturePlan(value.Input);
                if (plan == null || plan.TileKeys == null || plan.TileKeys.Length == 0)
                { Pending(value.Input, "NO_REQUIRED_TERRAIN"); continue; }
                // The broker captures generations atomically with its metadata.
                value.Input.Identity.TerrainRequestGeneration = plan.TerrainRequestGeneration;
                value.Input.Identity.TerrainDatabaseGeneration = plan.TerrainDatabaseGeneration;
                Job job = new Job { Input = value.Input, Plan = plan };
                if (!submitRead(plan, result => ReadCommitted(job, result)))
                { Pending(value.Input, "READ_ADMISSION_UNAVAILABLE"); continue; }
                jobs.Add(id, job);
                Emit("QUEUE", value.Input.Identity, "reason=ADMITTED cycle=" + cycle + " priority=" + value.Priority +
                    " eligible_best_rank=" + value.Priority + " in_flight=" + jobs.Count);
            }
            Summary();
        }

        bool Current(AERISTerrainCorridorComputeInput expected, AERISTerrainCorridorComputeInput current,
            AERISTerrainCorridorReadPlan plan, bool verifyTiles = true)
        {
            if (expected == null || current == null ||
                !string.Equals(expected.GameDataHash, current.GameDataHash, StringComparison.Ordinal)) return false;
            // A database append is content progress, not a new environment epoch.
            // Prove required-tile freshness, then compare every other identity field.
            AERISTerrainCorridorIdentity normalized = CopyIdentity(current.Identity);
            normalized.TerrainDatabaseGeneration = expected.Identity.TerrainDatabaseGeneration;
            return IdentityMatches(expected.Identity, normalized) && (!verifyTiles || isPlanCurrent(plan));
        }

        bool Current(Job job)
        {
            foreach (var value in Capture())
                if (string.Equals(value.Input.Identity.DirectionStableId, job.Input.Identity.DirectionStableId, StringComparison.OrdinalIgnoreCase))
                    return Current(job.Input, value.Input, job.Plan);
            return false;
        }

        void ReadCommitted(Job job, AERISTerrainCorridorReadResult result)
        {
            if (job.Retired || job.Phase != 0) return;
            job.Phase = 1;
            if (result == null) { Pending(job.Input, "READ_TERMINAL_NULL"); Retire(job); return; }
            if (!Current(job)) { Reject(job, "READ_IDENTITY_CHANGED"); return; }
            bool complete = result.TerrainCoverageComplete && job.Plan.TerrainCoverageComplete;
            Emit(complete ? "READ_READY" : "READ_INCOMPLETE", job.Input.Identity, "reason=" + result.FailureReason);
            job.Input.ReadResult = result;
            // Even partial analysis is shared-worker work; no AnalyzePure on main.
            if (!submitCompute(job.Input, snapshot => ComputeCommitted(job, snapshot)))
            { Pending(job.Input, "COMPUTE_ADMISSION_UNAVAILABLE"); Retire(job); }
        }

        void ComputeCommitted(Job job, AERISApproachObstacleSnapshot snapshot)
        {
            if (job.Retired || job.Phase != 1) return;
            job.Phase = 2;
            if (snapshot == null) { Pending(job.Input, "COMPUTE_TERMINAL_NULL"); Retire(job); return; }
            if (!Current(job)) { Reject(job, "COMPUTE_IDENTITY_CHANGED"); return; }
            Emit("WORKER_COMPLETE", job.Input.Identity, "samples=" + snapshot.Samples.Count);
            if (snapshot.Samples.Count > 0)
            {
                string id = job.Input.Identity.DirectionStableId;
                Publication old;
                bool changed = !published.TryGetValue(id, out old) ||
                    !IdentityMatches(old.Input.Identity, job.Input.Identity) ||
                    !string.Equals(old.Snapshot.TerrainSignature, snapshot.TerrainSignature, StringComparison.Ordinal) ||
                    old.Snapshot.TerrainCoverageComplete != snapshot.TerrainCoverageComplete ||
                    old.Snapshot.TerrainMissedApproachClear != snapshot.TerrainMissedApproachClear;
                if (changed)
                {
                    published[id] = new Publication { Input = job.Input, Plan = job.Plan, Snapshot = snapshot.Clone(),
                        ValidatedDatabaseGeneration = job.Input.Identity.TerrainDatabaseGeneration };
                    publicationGeneration++;
                    Emit("PUBLISH", job.Input.Identity, "terrain_coverage_complete=" + snapshot.TerrainCoverageComplete +
                        " obstacle_coverage_complete=false corridor_complete=false missed_approach_clear=false authority=NONE_PILOT");
                }
            }
            if (!snapshot.TerrainCoverageComplete || snapshot.Samples.Count == 0) Pending(job.Input, "TERRAIN_INCOMPLETE");
            Retire(job);
        }

        void Reject(Job job, string reason)
        {
            Emit("STALE_REJECT", job.Input.Identity, "reason=" + reason);
            Retire(job);
        }
        void Retire(Job job)
        {
            if (job.Retired) return;
            job.Retired = true;
            // Publications retain only snapshot samples and freshness metadata,
            // never the decoded tile payload after the worker has terminated.
            job.Input.ReadResult = null;
            Job admitted;
            if (jobs.TryGetValue(job.Input.Identity.DirectionStableId, out admitted) && object.ReferenceEquals(job, admitted))
                jobs.Remove(job.Input.Identity.DirectionStableId);
            Summary();
        }
        void Pending(AERISTerrainCorridorComputeInput input, string reason)
        {
            if (input.Identity.ProducerGeneration != producerGeneration) return;
            Attempt attempt;
            if (attempts.TryGetValue(input.Identity.DirectionStableId, out attempt)) attempt.Pending = true;
            Emit("DIRECTION_PENDING", input.Identity, "reason=" + reason);
        }
        void Summary()
        {
            int pending = 0;
            foreach (var attempt in attempts.Values) if (attempt.Pending) pending++;
            string summary = "in_flight=" + jobs.Count + " published=" + published.Count + " pending=" + pending + " authority=NONE_PILOT";
            if (summary == lastSummary) return;
            lastSummary = summary;
            Emit("SUMMARY", null, summary);
        }
        void Emit(string kind, AERISTerrainCorridorIdentity identity, string detail)
        {
            if (log != null) log("[AERIS54][LAND_R2] " + kind + " direction=" +
                (identity == null ? "ALL" : identity.DirectionStableId) + " body=" +
                (identity == null ? activeBody : identity.BodyName) + " environment=" +
                (identity == null ? "ALL" : identity.EnvironmentSignature) + " producer_generation=" +
                (identity == null ? producerGeneration : identity.ProducerGeneration) + " " + detail);
        }
        static AERISTerrainCorridorIdentity CopyIdentity(AERISTerrainCorridorIdentity value)
        {
            return new AERISTerrainCorridorIdentity {
                DirectionStableId = value.DirectionStableId, BodyName = value.BodyName,
                BodyRadiusMeters = value.BodyRadiusMeters, EnvironmentSignature = value.EnvironmentSignature,
                AirfieldDatabaseRevision = value.AirfieldDatabaseRevision, RunwayGeometryRevision = value.RunwayGeometryRevision,
                TerrainRequestGeneration = value.TerrainRequestGeneration, TerrainDatabaseGeneration = value.TerrainDatabaseGeneration,
                ProducerGeneration = value.ProducerGeneration
            };
        }

        static IList<AERISTerrainCorridorDirectionCapture> CaptureDirections(AERISAirfieldRegistry registry,
            AERISLandingFoundation landing, AERISTerrainCorridorReadService reads,
            AERISApproachPlanningLimits limits, string activeBody)
        {
            var result = new List<AERISTerrainCorridorDirectionCapture>();
            if (registry == null) return result;
            string selected = registry.SelectedDirection == null ? string.Empty : registry.SelectedDirection.StableId;
            string armed = landing == null || !landing.Armed || landing.ActiveDirection == null ? string.Empty : landing.ActiveDirection.StableId;
            IList<AERISAirfieldDefinition> airfields = registry.Airfields;
            foreach (var airfield in airfields)
            {
                // All body lookup and authoritative environment evaluation is main-thread only.
                CelestialBody body = null;
                if (FlightGlobals.Bodies != null)
                    foreach (var candidate in FlightGlobals.Bodies)
                        if (candidate != null && string.Equals(candidate.bodyName, airfield.Body, StringComparison.OrdinalIgnoreCase))
                        { body = candidate; break; }
                if (body == null || !IsFinite(body.Radius) || body.Radius <= 0) continue;
                double radius = body.Radius;
                string environment = AERISTerrainTileSystem.EnvironmentHashForBody(body);
                foreach (var runway in airfield.Runways)
                    foreach (var original in runway.Directions)
                    {
                        if (original == null || !original.IsCertified || !original.HasCertifiedGeometry || string.IsNullOrEmpty(original.StableId)) continue;
                        AERISRunwayDirectionDefinition direction = original.Clone();
                        var input = new AERISTerrainCorridorComputeInput {
                            Identity = new AERISTerrainCorridorIdentity {
                                DirectionStableId = direction.StableId, BodyName = airfield.Body,
                                BodyRadiusMeters = radius, EnvironmentSignature = environment,
                                AirfieldDatabaseRevision = registry.DatabaseRevision, RunwayGeometryRevision = direction.GeometryRevision,
                                TerrainRequestGeneration = reads == null ? -1 : reads.RequestGeneration,
                                TerrainDatabaseGeneration = reads == null ? -1 : reads.DatabaseGeneration
                            },
                            GameDataHash = AERISTerrainTileSystem.GameDataHash,
                            ThresholdLatitudeDeg = direction.Threshold.LatitudeDeg,
                            ThresholdLongitudeDeg = direction.Threshold.LongitudeDeg,
                            ThresholdElevationMeters = direction.Threshold.ElevationMeters,
                            InboundHeadingDeg = direction.HeadingDeg,
                            MissedApproachHeadingDeg = direction.MissedApproachHeadingDeg,
                            RequiredMissedApproachAltitudeMeters = Math.Max(direction.MissedApproachSafeAltitudeMeters, limits.MinimumMissedApproachAltitudeMeters),
                            Limits = limits.Clone()
                        };
                        result.Add(new AERISTerrainCorridorDirectionCapture { Input = input,
                            Priority = PriorityRank(string.Equals(armed, direction.StableId, StringComparison.OrdinalIgnoreCase),
                                string.Equals(selected, direction.StableId, StringComparison.OrdinalIgnoreCase),
                                string.Equals(activeBody, airfield.Body, StringComparison.OrdinalIgnoreCase)) });
                    }
            }
            return result;
        }

        static AERISTerrainCorridorReadPlan CapturePlan(AERISTerrainCorridorReadService reads,
            AERISTerrainCorridorComputeInput input, IList<AERISTerrainCorridorQueryPoint> points)
        {
            if (reads == null) return null;
            var id = input.Identity;
            AERISTerrainCorridorReadPlan plan;
            string reason;
            return reads.TryCapturePlan(id.DirectionStableId, id.BodyName, id.BodyRadiusMeters,
                id.EnvironmentSignature, input.GameDataHash, id.AirfieldDatabaseRevision,
                id.RunwayGeometryRevision, id.ProducerGeneration, points, out plan, out reason) ? plan : null;
        }

        static AERISTerrainCorridorReadPlan BuildPlan(AERISTerrainCorridorReadService reads,
            AERISTerrainCorridorComputeInput input)
        {
            List<AERISTerrainCorridorQueryPoint> points = BuildGrid(input, null);
            AERISTerrainCorridorReadPlan baseline = CapturePlan(reads, input, points);
            if (baseline == null) return null;
            List<AERISTerrainCorridorQueryPoint> refined = BuildGrid(input, baseline);
            return refined.Count == points.Count ? baseline : CapturePlan(reads, input, refined);
        }

        static List<AERISTerrainCorridorQueryPoint> BuildGrid(AERISTerrainCorridorComputeInput input,
            AERISTerrainCorridorReadPlan baseline)
        {
            var result = new List<AERISTerrainCorridorQueryPoint>();
            double maximum = input.Limits.MaximumCaptureDistanceMeters;
            double halfWidth = input.Limits.CorridorHalfWidthMeters;
            double farSpacing = AERISTerrainTileFormat.NominalCellMeters(AERISTerrainTileLod.Far);
            if (!IsFinite(maximum) || maximum <= 0 || !IsFinite(halfWidth) || halfWidth < 0) return result;
            var stations = new List<double>();
            for (double along = 0; along < maximum; along += farSpacing) stations.Add(along);
            stations.Add(maximum);
            for (int path = 0; path < 2; path++)
            {
                bool missed = path == 1;
                for (int station = 0; station < stations.Count; station++)
                {
                    double start = stations[station];
                    AddStation(result, input, start, missed, halfWidth);
                    if (baseline == null || station + 1 == stations.Count) continue;
                    double end = stations[station + 1];
                    int bestLod = (int)AERISTerrainTileLod.Far;
                    for (int p = 0; p < baseline.QueryPoints.Length; p++)
                    {
                        var point = baseline.QueryPoints[p];
                        if (point.MissedApproach == missed &&
                            (Math.Abs(point.AlongTrackMeters) == start || Math.Abs(point.AlongTrackMeters) == end))
                            bestLod = Math.Max(bestLod, baseline.PointSourceLods[p]);
                    }
                    double spacing = Math.Max(64.0, AERISTerrainTileFormat.NominalCellMeters((AERISTerrainTileLod)bestLod));
                    for (double along = start + spacing; along < end; along += spacing)
                        AddStation(result, input, along, missed, halfWidth);
                }
            }
            return result;
        }

        static void AddStation(List<AERISTerrainCorridorQueryPoint> points, AERISTerrainCorridorComputeInput input,
            double along, bool missed, double halfWidth)
        {
            for (int crossIndex = missed ? 0 : -1; crossIndex <= (missed ? 0 : 1); crossIndex++)
            {
                double cross = crossIndex * halfWidth;
                // Along-track is positive outward on final and negative on missed;
                // geodetic bearing uses the inbound reciprocal for final approach.
                double heading = (missed ? input.MissedApproachHeadingDeg : input.InboundHeadingDeg + 180.0) * Math.PI / 180.0;
                double crossHeading = input.InboundHeadingDeg * Math.PI / 180.0;
                double east = Math.Sin(heading) * along + Math.Cos(crossHeading) * cross;
                double north = Math.Cos(heading) * along - Math.Sin(crossHeading) * cross;
                double distance = Math.Sqrt(east * east + north * north) / input.Identity.BodyRadiusMeters;
                double bearing = Math.Atan2(east, north);
                double lat = input.ThresholdLatitudeDeg * Math.PI / 180.0;
                double lon = input.ThresholdLongitudeDeg * Math.PI / 180.0;
                double destinationLat = Math.Asin(Math.Max(-1.0, Math.Min(1.0,
                    Math.Sin(lat) * Math.Cos(distance) + Math.Cos(lat) * Math.Sin(distance) * Math.Cos(bearing))));
                double destinationLon = lon + Math.Atan2(Math.Sin(bearing) * Math.Sin(distance) * Math.Cos(lat),
                    Math.Cos(distance) - Math.Sin(lat) * Math.Sin(destinationLat));
                double longitude = destinationLon * 180.0 / Math.PI;
                longitude = ((longitude + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                points.Add(new AERISTerrainCorridorQueryPoint { Index = points.Count,
                    LatitudeDeg = destinationLat * 180.0 / Math.PI, LongitudeDeg = longitude,
                    AlongTrackMeters = missed ? -along : along, CrossTrackMeters = cross, MissedApproach = missed });
            }
        }

        const string TerrainOnlyObstacleSignature =
            "R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE";

        sealed class OrderedQuery
        {
            internal int Ordinal;
            internal AERISTerrainCorridorQueryPoint Point;
            internal string TileStableId;
            internal int SourceLod;
        }

        internal static int PriorityRank(bool armed, bool selected,
            bool activeBody)
        {
            if (armed) return 0;
            if (selected) return 1;
            return activeBody ? 2 : 3;
        }

        internal static bool IdentityMatches(
            AERISTerrainCorridorIdentity expected,
            AERISTerrainCorridorIdentity current)
        {
            if (expected == null || current == null) return false;
            return string.Equals(expected.DirectionStableId,
                       current.DirectionStableId, StringComparison.Ordinal) &&
                   string.Equals(expected.BodyName, current.BodyName,
                       StringComparison.Ordinal) &&
                   expected.BodyRadiusMeters == current.BodyRadiusMeters &&
                   string.Equals(expected.EnvironmentSignature,
                       current.EnvironmentSignature, StringComparison.Ordinal) &&
                   expected.AirfieldDatabaseRevision ==
                       current.AirfieldDatabaseRevision &&
                   expected.RunwayGeometryRevision ==
                       current.RunwayGeometryRevision &&
                   expected.TerrainRequestGeneration ==
                       current.TerrainRequestGeneration &&
                   expected.TerrainDatabaseGeneration ==
                       current.TerrainDatabaseGeneration &&
                   expected.ProducerGeneration == current.ProducerGeneration;
        }

        internal static AERISApproachObstacleSnapshot AnalyzePure(
            AERISTerrainCorridorComputeInput input)
        {
            AERISApproachObstacleSnapshot snapshot = CreateFailClosedSnapshot(input);
            AERISTerrainCorridorIdentity identity = input == null ? null :
                input.Identity;
            AERISTerrainCorridorReadResult result = input == null ? null :
                input.ReadResult;
            AERISTerrainCorridorReadPlan plan = result == null ? null : result.Plan;

            StringBuilder signature = new StringBuilder();
            signature.Append("R2|");
            signature.Append(identity == null ? string.Empty :
                identity.DirectionStableId ?? string.Empty);
            signature.Append('|');
            signature.Append(identity == null ? string.Empty :
                identity.BodyName ?? string.Empty);
            signature.Append('|');
            signature.Append(identity == null ? string.Empty :
                identity.EnvironmentSignature ?? string.Empty);
            signature.Append('|');
            signature.Append((identity == null ? 0L :
                identity.TerrainDatabaseGeneration).ToString(
                    CultureInfo.InvariantCulture));

            AERISTerrainCorridorQueryPoint[] points = plan == null ? null :
                plan.QueryPoints;
            string[] mappings = plan == null ? null : plan.PointTileStableIds;
            int[] sourceLods = plan == null ? null : plan.PointSourceLods;
            bool structureValid = result != null && plan != null &&
                result.Tiles != null && points != null && points.Length > 0 &&
                mappings != null && mappings.Length == points.Length &&
                sourceLods != null && sourceLods.Length == points.Length;
            bool terrainComplete = structureValid && result.TerrainCoverageComplete &&
                plan.TerrainCoverageComplete;
            bool haveMissedPoint = false;
            bool missedPointsClear = true;
            int bestObservedLod = -1;

            if (structureValid)
            {
                List<OrderedQuery> ordered = new List<OrderedQuery>(points.Length);
                for (int i = 0; i < points.Length; i++)
                {
                    ordered.Add(new OrderedQuery
                    {
                        Ordinal = i,
                        Point = points[i],
                        TileStableId = mappings[i] ?? string.Empty,
                        SourceLod = sourceLods[i]
                    });
                }
                ordered.Sort(CompareQueries);

                for (int i = 0; i < ordered.Count; i++)
                {
                    OrderedQuery query = ordered[i];
                    bool missed = query.Point != null &&
                        query.Point.MissedApproach;
                    if (missed) haveMissedPoint = true;

                    AERISTerrainHeightTile tile;
                    double elevation;
                    if (!TrySampleQuery(result.Tiles, query, out tile,
                        out elevation))
                    {
                        terrainComplete = false;
                        if (missed) missedPointsClear = false;
                        continue;
                    }

                    long elevationMillimetres;
                    if (!TryElevationMillimetres(elevation,
                        out elevationMillimetres))
                    {
                        terrainComplete = false;
                        if (missed) missedPointsClear = false;
                        continue;
                    }

                    snapshot.Samples.Add(new AERISApproachObstacleSample
                    {
                        AlongTrackMeters = query.Point.AlongTrackMeters,
                        CrossTrackMeters = query.Point.CrossTrackMeters,
                        TopElevationMeters = elevation,
                        HorizontalRadiusMeters = 0.0,
                        IsTerrain = true,
                        SourceId = tile.Key.StableId
                    });

                    // AERISTerrainTileLod numeric values increase with fidelity.
                    // Despite the legacy Minimum* field name, retain the maximum
                    // numeric LOD used: the best observed fidelity in this snapshot.
                    if (query.SourceLod > bestObservedLod)
                        bestObservedLod = query.SourceLod;
                    signature.Append('|');
                    signature.Append(query.Point.Index.ToString(
                        CultureInfo.InvariantCulture));
                    signature.Append('|');
                    signature.Append(tile.Key.StableId);
                    signature.Append('|');
                    signature.Append(query.SourceLod.ToString(
                        CultureInfo.InvariantCulture));
                    signature.Append('|');
                    signature.Append(elevationMillimetres.ToString(
                        CultureInfo.InvariantCulture));

                    if (missed && !(elevation <
                        input.RequiredMissedApproachAltitudeMeters))
                        missedPointsClear = false;
                }
            }

            snapshot.MinimumTerrainSourceLod = bestObservedLod;
            snapshot.TerrainCoverageComplete = terrainComplete;
            snapshot.TerrainMissedApproachClear = haveMissedPoint &&
                missedPointsClear;
            snapshot.TerrainSignature = AERISTerrainHash.Fnv1A64Hex(
                signature.ToString());
            return snapshot;
        }

        static AERISApproachObstacleSnapshot CreateFailClosedSnapshot(
            AERISTerrainCorridorComputeInput input)
        {
            AERISTerrainCorridorIdentity identity = input == null ? null :
                input.Identity;
            return new AERISApproachObstacleSnapshot
            {
                Generation = identity == null ? 0L : identity.ProducerGeneration,
                DirectionStableId = identity == null ? string.Empty :
                    identity.DirectionStableId ?? string.Empty,
                BodyName = identity == null ? string.Empty :
                    identity.BodyName ?? string.Empty,
                EnvironmentSignature = identity == null ? string.Empty :
                    identity.EnvironmentSignature ?? string.Empty,
                AirfieldDatabaseRevision = identity == null ? 0L :
                    identity.AirfieldDatabaseRevision,
                RunwayGeometryRevision = identity == null ? 0L :
                    identity.RunwayGeometryRevision,
                TerrainRequestGeneration = identity == null ? 0L :
                    identity.TerrainRequestGeneration,
                TerrainDatabaseGeneration = identity == null ? 0L :
                    identity.TerrainDatabaseGeneration,
                BodyRadiusMeters = identity == null ? 0.0 :
                    identity.BodyRadiusMeters,
                MissedApproachMinimumAltitudeMeters = input == null ? 0.0 :
                    input.RequiredMissedApproachAltitudeMeters,
                TerrainCoverageComplete = false,
                ObstacleCoverageComplete = false,
                MinimumTerrainSourceLod = -1,
                TerrainMissedApproachClear = false,
                ObstacleSignature = TerrainOnlyObstacleSignature,
                CorridorComplete = false,
                MissedApproachClear = false
            };
        }

        static int CompareQueries(OrderedQuery left, OrderedQuery right)
        {
            int leftIndex = left.Point == null ? int.MaxValue : left.Point.Index;
            int rightIndex = right.Point == null ? int.MaxValue : right.Point.Index;
            int byIndex = leftIndex.CompareTo(rightIndex);
            return byIndex != 0 ? byIndex : left.Ordinal.CompareTo(right.Ordinal);
        }

        static bool TrySampleQuery(
            Dictionary<string, AERISTerrainHeightTile> tiles,
            OrderedQuery query,
            out AERISTerrainHeightTile tile,
            out double elevation)
        {
            tile = null;
            elevation = 0.0;
            if (query == null || query.Point == null ||
                string.IsNullOrEmpty(query.TileStableId) ||
                query.SourceLod < (int)AERISTerrainTileLod.Global ||
                query.SourceLod > (int)AERISTerrainTileLod.Land ||
                !IsFinite(query.Point.LatitudeDeg) ||
                !IsFinite(query.Point.LongitudeDeg) ||
                !IsFinite(query.Point.AlongTrackMeters) ||
                !IsFinite(query.Point.CrossTrackMeters) ||
                !tiles.TryGetValue(query.TileStableId, out tile) || tile == null ||
                !string.Equals(tile.Key.StableId, query.TileStableId,
                    StringComparison.Ordinal) ||
                !tile.SamplingComplete || tile.Quality < 100)
                return false;

            int resolution = tile.Resolution;
            if (resolution < 2 || resolution > 46340 ||
                tile.Elevation == null ||
                tile.Elevation.Length != resolution * resolution ||
                !IsFinite(tile.SouthLatitudeDeg) ||
                !IsFinite(tile.NorthLatitudeDeg) ||
                !IsFinite(tile.WestLongitudeDeg) ||
                !IsFinite(tile.EastLongitudeDeg) ||
                !(tile.NorthLatitudeDeg > tile.SouthLatitudeDeg) ||
                !(tile.EastLongitudeDeg > tile.WestLongitudeDeg) ||
                query.Point.LatitudeDeg < tile.SouthLatitudeDeg ||
                query.Point.LatitudeDeg > tile.NorthLatitudeDeg ||
                query.Point.LongitudeDeg < tile.WestLongitudeDeg ||
                query.Point.LongitudeDeg > tile.EastLongitudeDeg)
                return false;

            double x = (query.Point.LongitudeDeg - tile.WestLongitudeDeg) /
                (tile.EastLongitudeDeg - tile.WestLongitudeDeg) *
                (resolution - 1);
            double y = (query.Point.LatitudeDeg - tile.SouthLatitudeDeg) /
                (tile.NorthLatitudeDeg - tile.SouthLatitudeDeg) *
                (resolution - 1);
            x = Math.Max(0.0, Math.Min(resolution - 1, x));
            y = Math.Max(0.0, Math.Min(resolution - 1, y));
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = Math.Min(resolution - 1, x0 + 1);
            int y1 = Math.Min(resolution - 1, y0 + 1);
            double z00 = tile.Elevation[y0 * resolution + x0];
            double z10 = tile.Elevation[y0 * resolution + x1];
            double z01 = tile.Elevation[y1 * resolution + x0];
            double z11 = tile.Elevation[y1 * resolution + x1];
            if (!IsFinite(z00) || !IsFinite(z10) || !IsFinite(z01) ||
                !IsFinite(z11)) return false;

            double tx = x - x0;
            double ty = y - y0;
            double south = z00 + (z10 - z00) * tx;
            double north = z01 + (z11 - z01) * tx;
            elevation = south + (north - south) * ty;
            return IsFinite(elevation);
        }

        static bool TryElevationMillimetres(double elevation,
            out long elevationMillimetres)
        {
            elevationMillimetres = 0L;
            double scaled = elevation * 1000.0;
            if (!IsFinite(scaled) || scaled < long.MinValue ||
                scaled > long.MaxValue) return false;
            elevationMillimetres = (long)Math.Round(scaled,
                MidpointRounding.AwayFromZero);
            return true;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
