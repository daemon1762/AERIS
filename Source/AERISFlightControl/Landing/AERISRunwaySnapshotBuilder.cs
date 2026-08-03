using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AERISFlightControl.Landing
{
    internal enum AERISRunwaySnapshotCaptureProgress
    {
        Pending = 0,
        Completed = 1,
        Failed = 2
    }

    // Main-thread-only Unity/KSP acquisition boundary.  The returned snapshot contains
    // primitive numbers, strings and copied arrays only.
    internal static class AERISRunwaySnapshotBuilder
    {
        const int MaximumPoints = 32768;
        const int MaximumPrimitives = 4096;
        const double CoordinateLimitMeters = 200000.0;

        internal sealed class LocalFrame
        {
            internal CelestialBody Body;
            internal Vector3 Origin;
            internal Vector3 East;
            internal Vector3 North;
            internal Vector3 Up;
            internal double Latitude;
            internal double Longitude;
            internal double Elevation;
            internal bool AbsolutePlacementRequired;
            internal bool AbsolutePlacementConstraintAvailable;
            internal double LaunchAnchorEastMeters;
            internal double LaunchAnchorNorthMeters;
            internal double LaunchAnchorUpMeters;
            internal double LaunchAnchorHeadingDeg;
            internal bool ProviderReferenceOriginUsed;
            internal double ProviderReferenceToLaunchMeters;
            internal bool PqsSampled;
            internal double PqsElevation;
        }

        sealed class WitnessFrame
        {
            internal bool Available;
            internal bool UserCalibrated;
            internal bool UserCalibrationPresent;
            internal bool UserCalibrationPending;
            internal bool PlacementMismatchObserved;
            internal string PlacementObservationDetail = string.Empty;
            internal string Source = string.Empty;
            internal string Name = string.Empty;
            internal string SourcePath = string.Empty;
            internal double StartEast;
            internal double StartNorth;
            internal double StartUp;
            internal double EndEast;
            internal double EndNorth;
            internal double EndUp;
            internal double HeadingDeg;
            internal double LengthMeters;
            internal double MatchDistanceMeters;
            internal double Confidence;
            internal string Fingerprint = string.Empty;
        }

        // Incremental main-thread acquisition.  Unity object access stays here, while
        // every worker-bound value is copied into numeric DTOs.  Expensive Unity calls
        // cannot be pre-empted mid-call, so a single mesh may exceed the target; such an
        // overrun is measured and reported, and no second component is processed in that
        // frame once the 1.50 ms hard slice has been crossed.
        internal sealed class Capture
        {
            const double TargetSliceMilliseconds = 0.50;
            const double HardSliceMilliseconds = 1.50;
            readonly AERISProviderFacilityRecord record;
            readonly AERISRunwaySurveyDefinition definition;
            readonly AERISRunwayWitness witness;
            readonly long generation;
            readonly long sequence;
            readonly LocalFrame frame;
            readonly List<AERISSurveyPoint> points = new List<AERISSurveyPoint>(2048);
            readonly List<AERISSurveyPrimitive> primitives =
                new List<AERISSurveyPrimitive>(64);
            Collider[] liveColliders;
            MeshFilter[] liveFilters;
            Renderer[] liveRenderers;
            MeshFilter[] prefabFilters;
            Collider[] prefabColliders;
            readonly List<string> canonicalSourceComponents = new List<string>(64);
            readonly bool canonicalUsesPrefab;
            int phase;
            int index;
            int sourceGroup = 1;
            bool colliderReadable;
            bool geometryReadable;
            int slices;
            int hardOverruns;
            double maximumSliceMilliseconds;

            internal Capture(AERISProviderFacilityRecord record,
                AERISRunwaySurveyDefinition definition, AERISRunwayWitness witness,
                long generation, long sequence, LocalFrame frame)
            {
                this.record = record;
                this.definition = definition;
                this.witness = witness == null ? null : witness.Clone();
                this.generation = generation;
                this.sequence = sequence;
                this.frame = frame;
                canonicalUsesPrefab = record != null &&
                    record.RuntimeRunwayPrefab != null;
            }

            internal string Status
            {
                get
                {
                    return PhaseName(phase) + " | points " + points.Count +
                        " | primitives " + primitives.Count + " | max-slice " +
                        maximumSliceMilliseconds.ToString("0.000",
                            CultureInfo.InvariantCulture) + " ms";
                }
            }

            internal int SliceCount { get { return slices; } }
            internal int HardOverrunCount { get { return hardOverruns; } }
            internal double MaximumSliceMilliseconds
            {
                get { return maximumSliceMilliseconds; }
            }

            internal AERISRunwaySnapshotCaptureProgress Tick(
                out AERISRunwaySurveySnapshot snapshot,
                out AERISRunwayFailureCode failure, out string detail)
            {
                snapshot = null;
                failure = AERISRunwayFailureCode.None;
                detail = string.Empty;
                var watch = Stopwatch.StartNew();
                slices++;
                int operations = 0;
                try
                {
                    while (operations == 0 ||
                        watch.Elapsed.TotalMilliseconds < TargetSliceMilliseconds)
                    {
                        AERISRunwaySnapshotCaptureProgress progress = Step(
                            out snapshot, out failure, out detail);
                        operations++;
                        if (progress != AERISRunwaySnapshotCaptureProgress.Pending)
                        {
                            RecordSlice(watch.Elapsed.TotalMilliseconds);
                            if (progress == AERISRunwaySnapshotCaptureProgress.Completed)
                                detail += "; slices=" + slices + "; maxSliceMs=" +
                                    maximumSliceMilliseconds.ToString("0.000",
                                        CultureInfo.InvariantCulture) +
                                    "; hardOverruns=" + hardOverruns;
                            return progress;
                        }
                        if (watch.Elapsed.TotalMilliseconds >= HardSliceMilliseconds) break;
                    }
                    RecordSlice(watch.Elapsed.TotalMilliseconds);
                    detail = Status;
                    return AERISRunwaySnapshotCaptureProgress.Pending;
                }
                catch (Exception ex)
                {
                    RecordSlice(watch.Elapsed.TotalMilliseconds);
                    failure = AERISRunwayFailureCode.MeshUnreadable;
                    detail = "INCREMENTAL SNAPSHOT FAILED IN " + PhaseName(phase) +
                        ": " + ex.GetType().Name + " — " + ex.Message;
                    return AERISRunwaySnapshotCaptureProgress.Failed;
                }
            }

            AERISRunwaySnapshotCaptureProgress Step(
                out AERISRunwaySurveySnapshot snapshot,
                out AERISRunwayFailureCode failure, out string detail)
            {
                snapshot = null;
                failure = AERISRunwayFailureCode.None;
                detail = string.Empty;
                switch (phase)
                {
                    case 0:
                        liveColliders = record.RuntimeRunwayObject == null ?
                            new Collider[0] : record.RuntimeRunwayObject
                                .GetComponentsInChildren<Collider>(true);
                        phase = 1;
                        index = 0;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 1:
                        if (index < liveColliders.Length)
                        {
                            Collider collider = liveColliders[index++];
                            if (!canonicalUsesPrefab && collider != null &&
                                record.RuntimeRunwayObject != null)
                            {
                                string canonical = ColliderAssetIdentity(
                                    record.RuntimeRunwayObject.transform, collider);
                                if (!string.IsNullOrEmpty(canonical))
                                    canonicalSourceComponents.Add(canonical);
                            }
                            if (collider != null && !collider.isTrigger &&
                                collider.bounds.size.sqrMagnitude >= 0.01f &&
                                primitives.Count < MaximumPrimitives)
                            {
                                AERISSurveySemantic semantic = ClassifySemantic(collider.name,
                                    collider.transform == null ? string.Empty :
                                    collider.transform.name, true);
                                if (TryAddBoundsPrimitive(collider.bounds, frame, semantic,
                                    AERISRunwayEvidenceFamily.GeometryTopology,
                                    AERISRunwayMeasurementMethod.M04Collider,
                                    sourceGroup++, points, primitives))
                                {
                                    colliderReadable = true;
                                    geometryReadable = true;
                                }
                            }
                            return AERISRunwaySnapshotCaptureProgress.Pending;
                        }
                        liveColliders = null;
                        phase = 2;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 2:
                        liveFilters = record.RuntimeRunwayObject == null ?
                            new MeshFilter[0] : record.RuntimeRunwayObject
                                .GetComponentsInChildren<MeshFilter>(true);
                        phase = 3;
                        index = 0;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 3:
                        if (index < liveFilters.Length)
                        {
                            MeshFilter filter = liveFilters[index++];
                            if (!canonicalUsesPrefab && filter != null &&
                                record.RuntimeRunwayObject != null)
                            {
                                string canonical = MeshAssetIdentity(
                                    record.RuntimeRunwayObject.transform, filter);
                                if (!string.IsNullOrEmpty(canonical))
                                    canonicalSourceComponents.Add(canonical);
                            }
                            if (filter != null && filter.sharedMesh != null &&
                                primitives.Count < MaximumPrimitives)
                            {
                                Renderer renderer = filter.GetComponent<Renderer>();
                                AERISSurveySemantic semantic = ClassifySemantic(filter.name,
                                    MaterialNames(renderer), true);
                                if (TryAddMesh(filter.sharedMesh,
                                    filter.transform.localToWorldMatrix, frame, semantic,
                                    sourceGroup++, points, primitives)) geometryReadable = true;
                            }
                            return AERISRunwaySnapshotCaptureProgress.Pending;
                        }
                        liveFilters = null;
                        phase = 4;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 4:
                        liveRenderers = geometryReadable || record.RuntimeRunwayObject == null ?
                            new Renderer[0] : record.RuntimeRunwayObject
                                .GetComponentsInChildren<Renderer>(true);
                        phase = 5;
                        index = 0;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 5:
                        if (index < liveRenderers.Length)
                        {
                            Renderer renderer = liveRenderers[index++];
                            if (renderer != null && renderer.bounds.size.sqrMagnitude >= 0.01f &&
                                primitives.Count < MaximumPrimitives)
                            {
                                AERISSurveySemantic semantic = ClassifySemantic(renderer.name,
                                    MaterialNames(renderer), true);
                                if (TryAddBoundsPrimitive(renderer.bounds, frame, semantic,
                                    AERISRunwayEvidenceFamily.GeometryTopology,
                                    AERISRunwayMeasurementMethod.M02RendererBounds,
                                    sourceGroup++, points, primitives)) geometryReadable = true;
                            }
                            return AERISRunwaySnapshotCaptureProgress.Pending;
                        }
                        liveRenderers = null;
                        phase = 6;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 6:
                        prefabFilters = record.RuntimeRunwayPrefab == null ?
                            new MeshFilter[0] : record.RuntimeRunwayPrefab
                                .GetComponentsInChildren<MeshFilter>(true);
                        phase = 7;
                        index = 0;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 7:
                        if (index < prefabFilters.Length)
                        {
                            MeshFilter filter = prefabFilters[index++];
                            if (filter != null && record.RuntimeRunwayPrefab != null)
                            {
                                string canonical = MeshAssetIdentity(
                                    record.RuntimeRunwayPrefab.transform, filter);
                                if (!string.IsNullOrEmpty(canonical))
                                    canonicalSourceComponents.Add(canonical);
                            }
                            if (filter != null && filter.sharedMesh != null &&
                                record.RuntimeInstanceTransform != null &&
                                primitives.Count < MaximumPrimitives)
                            {
                                float safeScale = record.RuntimeModelScale > 0f &&
                                    !float.IsNaN(record.RuntimeModelScale) &&
                                    !float.IsInfinity(record.RuntimeModelScale)
                                        ? record.RuntimeModelScale : 1f;
                                Matrix4x4 toRoot = record.RuntimeRunwayPrefab.transform
                                    .worldToLocalMatrix * filter.transform.localToWorldMatrix;
                                Matrix4x4 world = record.RuntimeInstanceTransform.localToWorldMatrix *
                                    Matrix4x4.Scale(Vector3.one * safeScale) * toRoot;
                                Renderer renderer = filter.GetComponent<Renderer>();
                                AERISSurveySemantic semantic = ClassifySemantic(filter.name,
                                    MaterialNames(renderer), true);
                                if (TryAddMesh(filter.sharedMesh, world, frame, semantic,
                                    sourceGroup++, points, primitives)) geometryReadable = true;
                            }
                            return AERISRunwaySnapshotCaptureProgress.Pending;
                        }
                        prefabFilters = null;
                        phase = 8;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 8:
                        prefabColliders = !canonicalUsesPrefab ||
                            record.RuntimeRunwayPrefab == null ? new Collider[0] :
                            record.RuntimeRunwayPrefab.GetComponentsInChildren<Collider>(true);
                        phase = 9;
                        index = 0;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    case 9:
                        if (index < prefabColliders.Length)
                        {
                            Collider collider = prefabColliders[index++];
                            string canonical = ColliderAssetIdentity(
                                record.RuntimeRunwayPrefab.transform, collider);
                            if (!string.IsNullOrEmpty(canonical))
                                canonicalSourceComponents.Add(canonical);
                            return AERISRunwaySnapshotCaptureProgress.Pending;
                        }
                        prefabColliders = null;
                        phase = 10;
                        return AERISRunwaySnapshotCaptureProgress.Pending;
                    default:
                        geometryReadable = geometryReadable &&
                            (primitives.Count > 0 || points.Count >= 8);
                        if (!geometryReadable)
                        {
                            failure = colliderReadable
                                ? AERISRunwayFailureCode.NoGeometryEvidence
                                : AERISRunwayFailureCode.ColliderUnavailable;
                            detail = "NO READABLE RENDERER, COLLIDER OR MESH GEOMETRY";
                            return AERISRunwaySnapshotCaptureProgress.Failed;
                        }
                        AERISRunwaySurveyDefinition limits = definition ??
                            new AERISRunwaySurveyDefinition
                            {
                                Method = AERISRunwaySurveyMethod.ConsensusAutomatic
                            };
                        string canonicalSourceFingerprint = CanonicalSourceGeometryFingerprint(
                            record, canonicalSourceComponents);
                        string sourceFingerprint = BuildSourceFingerprint(record, limits,
                            canonicalSourceFingerprint, witness);
                        string fingerprint = BuildFingerprint(record, limits, frame,
                            sourceFingerprint, witness);
                        snapshot = BuildSnapshot(record, limits, frame, generation, sequence,
                            points, primitives, colliderReadable, witness, sourceFingerprint, fingerprint);
                        detail = "SNAPSHOT points=" + points.Count + "; primitives=" +
                            primitives.Count + "; fingerprint=" + fingerprint;
                        phase = 11;
                        return AERISRunwaySnapshotCaptureProgress.Completed;
                }
            }

            void RecordSlice(double milliseconds)
            {
                if (!Finite(milliseconds)) return;
                maximumSliceMilliseconds = Math.Max(maximumSliceMilliseconds, milliseconds);
                if (milliseconds > HardSliceMilliseconds) hardOverruns++;
            }

            static string PhaseName(int value)
            {
                switch (value)
                {
                    case 0: return "ENUMERATE COLLIDERS";
                    case 1: return "COPY COLLIDERS";
                    case 2: return "ENUMERATE LIVE MESHES";
                    case 3: return "COPY LIVE MESH";
                    case 4: return "ENUMERATE RENDERERS";
                    case 5: return "COPY RENDERER BOUNDS";
                    case 6: return "ENUMERATE PREFAB MESHES";
                    case 7: return "COPY PREFAB MESH";
                    case 8: return "ENUMERATE PREFAB COLLIDERS";
                    case 9: return "COPY PREFAB COLLIDER ID";
                    case 10: return "FINGERPRINT";
                    default: return "COMPLETE";
                }
            }
        }

        internal static bool TryBeginCapture(AERISProviderFacilityRecord record,
            AERISRunwaySurveyDefinition definition, long generation, long sequence,
            out Capture capture, out AERISRunwayFailureCode failure, out string detail)
        {
            return TryBeginCapture(record, definition, null, generation, sequence,
                out capture, out failure, out detail);
        }

        internal static bool TryBeginCapture(AERISProviderFacilityRecord record,
            AERISRunwaySurveyDefinition definition, AERISRunwayWitness witness,
            long generation, long sequence, out Capture capture,
            out AERISRunwayFailureCode failure, out string detail)
        {
            capture = null;
            failure = AERISRunwayFailureCode.None;
            detail = string.Empty;
            if (record == null)
            {
                failure = AERISRunwayFailureCode.ProviderDataError;
                detail = "PROVIDER RECORD IS NULL";
                return false;
            }
            if (record.FacilityKind != AERISFacilityKind.Runway ||
                ExplicitlyConflictsWithRunway(record.ProviderCategory,
                    record.ProviderSiteType))
            {
                failure = AERISRunwayFailureCode.FacilityCategoryConflict;
                detail = "PROVIDER CATEGORY IS NOT FIXED-WING RUNWAY";
                return false;
            }
            LocalFrame frame;
            if (!TryBuildFrame(record, out frame, out detail))
            {
                failure = AERISRunwayFailureCode.ModelUnavailable;
                return false;
            }
            capture = new Capture(record, definition, witness, generation, sequence, frame);
            detail = "SNAPSHOT CAPTURE STARTED";
            return true;
        }

        static AERISRunwaySurveySnapshot BuildSnapshot(
            AERISProviderFacilityRecord record, AERISRunwaySurveyDefinition limits,
            LocalFrame frame, long generation, long sequence,
            IList<AERISSurveyPoint> points, IList<AERISSurveyPrimitive> primitives,
            bool colliderReadable, AERISRunwayWitness witness,
            string sourceFingerprint, string fingerprint)
        {
            WitnessFrame witnessFrame = BuildWitnessFrame(frame, witness);
            return new AERISRunwaySurveySnapshot(generation, sequence,
                StableRecordId(record), record.ProviderUuid, record.ProviderSiteId,
                record.ProviderGroup, record.ProviderCategory, record.ProviderVersion,
                record.SourcePath, record.ModelName, record.Body,
                frame.Body == null ? 600000.0 : frame.Body.Radius,
                frame.Latitude, frame.Longitude, frame.Elevation,
                Math.Max(0.0, record.DeclaredLengthMeters),
                Math.Max(0.0, record.DeclaredWidthMeters),
                record.OrientationHeadingDeg,
                frame.AbsolutePlacementRequired,
                frame.AbsolutePlacementConstraintAvailable,
                frame.LaunchAnchorEastMeters, frame.LaunchAnchorNorthMeters,
                frame.LaunchAnchorUpMeters, frame.LaunchAnchorHeadingDeg,
                frame.ProviderReferenceOriginUsed,
                frame.ProviderReferenceToLaunchMeters,
                Math.Max(50.0, limits.MinimumLengthMeters),
                Math.Max(limits.MinimumLengthMeters, limits.MaximumLengthMeters),
                Math.Max(3.0, limits.MinimumWidthMeters),
                Math.Max(limits.MinimumWidthMeters, limits.MaximumWidthMeters),
                Math.Max(2.0, limits.MinimumAspectRatio), limits.Surface,
                string.IsNullOrEmpty(limits.SourceMod) ? record.SourceMod : limits.SourceMod,
                limits.Method, true, true, colliderReadable, frame.PqsSampled, frame.PqsElevation,
                true, true,
                ToArray(points), ToArray(primitives),
                witnessFrame.Available, witnessFrame.UserCalibrated,
                witnessFrame.UserCalibrationPresent,
                witnessFrame.UserCalibrationPending,
                witnessFrame.PlacementMismatchObserved,
                witnessFrame.PlacementObservationDetail,
                witnessFrame.Source, witnessFrame.Name, witnessFrame.SourcePath,
                witnessFrame.StartEast, witnessFrame.StartNorth, witnessFrame.StartUp,
                witnessFrame.EndEast, witnessFrame.EndNorth, witnessFrame.EndUp,
                witnessFrame.HeadingDeg, witnessFrame.LengthMeters,
                witnessFrame.MatchDistanceMeters, witnessFrame.Confidence,
                witnessFrame.Fingerprint, sourceFingerprint, fingerprint);
        }

        static WitnessFrame BuildWitnessFrame(LocalFrame frame,
            AERISRunwayWitness witness)
        {
            var value = new WitnessFrame();
            if (witness != null && witness.UserCalibrated)
            {
                value.UserCalibrationPresent = true;
                value.UserCalibrationPending = !witness.IsUsable;
                value.PlacementMismatchObserved = witness.PlacementMismatchObserved;
                value.PlacementObservationDetail =
                    witness.PlacementObservationDetail ?? string.Empty;
            }
            if (frame == null || frame.Body == null || witness == null || !witness.IsUsable)
                return value;
            double startEast;
            double startNorth;
            double endEast;
            double endNorth;
            if (!TryProjectBodyFixedGeodetic(frame, witness.Start,
                    out startEast, out startNorth) ||
                !TryProjectBodyFixedGeodetic(frame, witness.End,
                    out endEast, out endNorth))
                return value;
            value.Available = true;
            value.UserCalibrated = witness.UserCalibrated;
            value.Source = witness.Source ?? string.Empty;
            value.Name = witness.Name ?? string.Empty;
            value.SourcePath = witness.SourcePath ?? string.Empty;
            // A/B latitude and longitude are body-fixed absolute coordinates.  Do not
            // pass them through Unity world vectors or the provider's handed local basis:
            // that path mirrored east/west on KSP bodies and changed 195.85 deg into
            // 163.67 deg at Kola.  Spherical inverse projection preserves the marked
            // geodetic endpoints independently of floating origin and body rotation.
            value.StartEast = startEast;
            value.StartNorth = startNorth;
            value.StartUp = witness.Start.ElevationMeters - frame.Elevation;
            value.EndEast = endEast;
            value.EndNorth = endNorth;
            value.EndUp = witness.End.ElevationMeters - frame.Elevation;
            value.HeadingDeg = witness.HeadingDeg;
            value.LengthMeters = witness.LengthMeters;
            value.MatchDistanceMeters = witness.MatchDistanceMeters;
            value.Confidence = witness.Confidence;
            value.Fingerprint = witness.Fingerprint ?? string.Empty;
            return value;
        }


        static bool TryProjectBodyFixedGeodetic(LocalFrame frame, AERISGeoPoint point,
            out double eastMeters, out double northMeters)
        {
            eastMeters = northMeters = 0.0;
            if (frame == null || frame.Body == null || point == null || !point.IsFinite ||
                !Finite(frame.Latitude) || !Finite(frame.Longitude)) return false;
            double radius = Math.Max(1.0, frame.Body.Radius);
            double lat1 = frame.Latitude * Math.PI / 180.0;
            double lat2 = point.LatitudeDeg * Math.PI / 180.0;
            double dLon = NormalizeSignedLongitude(point.LongitudeDeg -
                frame.Longitude) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double bearing = Math.Atan2(y, x);
            double dLat = lat2 - lat1;
            double sinLat = Math.Sin(dLat * 0.5);
            double sinLon = Math.Sin(dLon * 0.5);
            double haversine = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) *
                sinLon * sinLon;
            haversine = Math.Max(0.0, Math.Min(1.0, haversine));
            double angle = 2.0 * Math.Atan2(Math.Sqrt(haversine),
                Math.Sqrt(Math.Max(0.0, 1.0 - haversine)));
            double distance = radius * angle;
            eastMeters = Math.Sin(bearing) * distance;
            northMeters = Math.Cos(bearing) * distance;
            return Finite(eastMeters) && Finite(northMeters) &&
                Math.Abs(eastMeters) <= CoordinateLimitMeters &&
                Math.Abs(northMeters) <= CoordinateLimitMeters;
        }

        static double NormalizeSignedLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        static AERISSurveyPoint[] ToArray(IList<AERISSurveyPoint> values)
        {
            var result = new AERISSurveyPoint[values == null ? 0 : values.Count];
            for (int i = 0; i < result.Length; i++) result[i] = values[i];
            return result;
        }

        static AERISSurveyPrimitive[] ToArray(IList<AERISSurveyPrimitive> values)
        {
            var result = new AERISSurveyPrimitive[values == null ? 0 : values.Count];
            for (int i = 0; i < result.Length; i++) result[i] = values[i];
            return result;
        }

        internal static bool TryBuild(AERISProviderFacilityRecord record,
            AERISRunwaySurveyDefinition definition, long generation, long sequence,
            out AERISRunwaySurveySnapshot snapshot, out AERISRunwayFailureCode failure,
            out string detail)
        {
            snapshot = null;
            failure = AERISRunwayFailureCode.None;
            detail = string.Empty;
            if (record == null)
            {
                failure = AERISRunwayFailureCode.ProviderDataError;
                detail = "PROVIDER RECORD IS NULL";
                return false;
            }
            if (record.FacilityKind != AERISFacilityKind.Runway ||
                ExplicitlyConflictsWithRunway(record.ProviderCategory,
                    record.ProviderSiteType))
            {
                failure = AERISRunwayFailureCode.FacilityCategoryConflict;
                detail = "PROVIDER CATEGORY IS NOT FIXED-WING RUNWAY";
                return false;
            }
            LocalFrame frame;
            if (!TryBuildFrame(record, out frame, out detail))
            {
                failure = AERISRunwayFailureCode.ModelUnavailable;
                return false;
            }

            var points = new List<AERISSurveyPoint>(2048);
            var primitives = new List<AERISSurveyPrimitive>(64);
            int sourceGroup = 1;
            bool colliderReadable = false;
            bool geometryReadable = false;
            try
            {
                if (record.RuntimeRunwayObject != null)
                {
                    AddLiveGeometry(record.RuntimeRunwayObject, frame, points, primitives,
                        ref sourceGroup, ref colliderReadable, ref geometryReadable);
                }
                if (record.RuntimeRunwayPrefab != null && record.RuntimeInstanceTransform != null)
                {
                    AddPrefabGeometry(record.RuntimeRunwayPrefab,
                        record.RuntimeInstanceTransform, record.RuntimeModelScale, frame,
                        points, primitives, ref sourceGroup, ref geometryReadable);
                }
            }
            catch (Exception ex)
            {
                failure = AERISRunwayFailureCode.MeshUnreadable;
                detail = "GEOMETRY SNAPSHOT FAILED: " + ex.GetType().Name;
                return false;
            }

            // Provider length/width/heading are priors, never physical geometry.  In
            // particular, do not manufacture a long rectangle from metadata: doing so
            // could certify a launch transform or whole-site platform when every real
            // mesh/collider is unreadable.  The declared values remain available in the
            // immutable snapshot for orientation and agreement tests in the worker.
            geometryReadable = geometryReadable &&
                (primitives.Count > 0 || points.Count >= 8);
            if (!geometryReadable)
            {
                failure = colliderReadable ? AERISRunwayFailureCode.NoGeometryEvidence :
                    AERISRunwayFailureCode.ColliderUnavailable;
                detail = "NO READABLE RENDERER, COLLIDER OR MESH GEOMETRY";
                return false;
            }

            AERISRunwaySurveyDefinition limits = definition ??
                new AERISRunwaySurveyDefinition { Method = AERISRunwaySurveyMethod.ConsensusAutomatic };
            string canonicalSourceFingerprint = CanonicalSourceGeometryFingerprint(record);
            string sourceFingerprint = BuildSourceFingerprint(record, limits,
                canonicalSourceFingerprint, null);
            string fingerprint = BuildFingerprint(record, limits, frame,
                sourceFingerprint, null);
            snapshot = new AERISRunwaySurveySnapshot(generation, sequence,
                StableRecordId(record), record.ProviderUuid, record.ProviderSiteId,
                record.ProviderGroup, record.ProviderCategory, record.ProviderVersion,
                record.SourcePath, record.ModelName, record.Body,
                frame.Body == null ? 600000.0 : frame.Body.Radius,
                frame.Latitude, frame.Longitude, frame.Elevation,
                Math.Max(0.0, record.DeclaredLengthMeters),
                Math.Max(0.0, record.DeclaredWidthMeters),
                record.OrientationHeadingDeg,
                frame.AbsolutePlacementRequired,
                frame.AbsolutePlacementConstraintAvailable,
                frame.LaunchAnchorEastMeters, frame.LaunchAnchorNorthMeters,
                frame.LaunchAnchorUpMeters, frame.LaunchAnchorHeadingDeg,
                frame.ProviderReferenceOriginUsed,
                frame.ProviderReferenceToLaunchMeters,
                Math.Max(50.0, limits.MinimumLengthMeters),
                Math.Max(limits.MinimumLengthMeters, limits.MaximumLengthMeters),
                Math.Max(3.0, limits.MinimumWidthMeters),
                Math.Max(limits.MinimumWidthMeters, limits.MaximumWidthMeters),
                Math.Max(2.0, limits.MinimumAspectRatio), limits.Surface,
                string.IsNullOrEmpty(limits.SourceMod) ? record.SourceMod : limits.SourceMod,
                limits.Method, true, geometryReadable, colliderReadable, frame.PqsSampled,
                frame.PqsElevation, true, true,
                points.ToArray(), primitives.ToArray(),
                false, false, false, false, false, string.Empty,
                string.Empty, string.Empty, string.Empty,
                0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
                0.0, 0.0, 0.0, 0.0, string.Empty,
                sourceFingerprint, fingerprint);
            detail = "SNAPSHOT points=" + points.Count + "; primitives=" +
                primitives.Count + "; fingerprint=" + fingerprint;
            return true;
        }

        static bool TryBuildFrame(AERISProviderFacilityRecord record, out LocalFrame frame,
            out string detail)
        {
            frame = null;
            detail = "RUNWAY LOCAL FRAME UNAVAILABLE";
            CelestialBody body = record.RuntimeBody;
            if (body == null || !record.RuntimeLaunchFrameValid) return false;
            Vector3 launchOrigin = record.RuntimeLaunchPosition;
            Vector3 origin = launchOrigin;
            bool providerOriginUsed = false;
            try
            {
                // KK/SLE statics have two distinct absolute anchors: the placed static
                // instance origin and the launch/spawn transform.  Use the instance
                // origin as the survey reference and retain the launch transform as an
                // independent centerline constraint.  The previous implementation used
                // the launch transform as both origin and evidence, so a bad spawn
                // transform could shift an otherwise stable physical runway registration.
                if (RequiresAbsolutePlacementConstraint(record))
                {
                    if (record.RuntimeInstanceTransform != null &&
                        FiniteVector(record.RuntimeInstanceTransform.position))
                    {
                        origin = record.RuntimeInstanceTransform.position;
                        providerOriginUsed = true;
                    }
                    else if (record.ProviderReferencePositionValid &&
                        Finite(record.LatitudeDeg) && Finite(record.LongitudeDeg) &&
                        Finite(record.ElevationMeters))
                    {
                        origin = (Vector3)body.GetWorldSurfacePosition(record.LatitudeDeg,
                            record.LongitudeDeg, record.ElevationMeters);
                        providerOriginUsed = FiniteVector(origin);
                    }
                }
                double latitude = body.GetLatitude(origin);
                double longitude = AERISAirfieldConfigParser.NormalizeLongitude(
                    body.GetLongitude(origin));
                double elevation = body.GetAltitude(origin);
                if (!Finite(latitude) || !Finite(longitude) || !Finite(elevation) ||
                    latitude < -90.0 || latitude > 90.0) return false;
                Vector3 up = ((Vector3)body.GetSurfaceNVector(latitude, longitude)).normalized;
                const double delta = 0.0001;
                Vector3 northPoint = (Vector3)body.GetWorldSurfacePosition(
                    Math.Min(89.9999, latitude + delta), longitude, elevation);
                Vector3 eastPoint = (Vector3)body.GetWorldSurfacePosition(latitude,
                    AERISAirfieldConfigParser.NormalizeLongitude(longitude + delta), elevation);
                Vector3 north = Vector3.ProjectOnPlane(northPoint - origin, up).normalized;
                Vector3 east = Vector3.ProjectOnPlane(eastPoint - origin, up).normalized;
                if (north.sqrMagnitude < 0.5f || east.sqrMagnitude < 0.5f)
                {
                    Vector3 forward = Vector3.ProjectOnPlane(record.RuntimeLaunchForward, up).normalized;
                    if (forward.sqrMagnitude < 0.5f) return false;
                    north = forward;
                    east = Vector3.Cross(up, north).normalized;
                }
                // Gram-Schmidt removes finite-difference non-orthogonality.
                east = Vector3.Cross(north, up).normalized;
                north = Vector3.Cross(up, east).normalized;

                Vector3 launchDelta = launchOrigin - origin;
                double launchEast = Vector3.Dot(launchDelta, east);
                double launchNorth = Vector3.Dot(launchDelta, north);
                double launchUp = Vector3.Dot(launchDelta, up);
                Vector3 launchForward = Vector3.ProjectOnPlane(
                    record.RuntimeLaunchForward, up).normalized;
                bool launchConstraint = RequiresAbsolutePlacementConstraint(record) &&
                    launchForward.sqrMagnitude > 0.5f && Finite(launchEast) &&
                    Finite(launchNorth) && Finite(launchUp);
                double launchHeading = launchConstraint
                    ? AERISAirfieldConfigParser.NormalizeHeading(Math.Atan2(
                        Vector3.Dot(launchForward, east),
                        Vector3.Dot(launchForward, north)) * 180.0 / Math.PI)
                    : record.OrientationHeadingDeg;
                double providerToLaunch = Math.Sqrt(launchEast * launchEast +
                    launchNorth * launchNorth);

                double pqsElevation;
                bool pqsSampled = AERISOperationalRunwayResolver.TryTerrainSample(body,
                    latitude, longitude, out pqsElevation);
                frame = new LocalFrame
                {
                    Body = body,
                    Origin = origin,
                    East = east,
                    North = north,
                    Up = up,
                    Latitude = latitude,
                    Longitude = longitude,
                    Elevation = elevation,
                    AbsolutePlacementRequired = RequiresAbsolutePlacementConstraint(record),
                    AbsolutePlacementConstraintAvailable = launchConstraint,
                    LaunchAnchorEastMeters = launchEast,
                    LaunchAnchorNorthMeters = launchNorth,
                    LaunchAnchorUpMeters = launchUp,
                    LaunchAnchorHeadingDeg = launchHeading,
                    ProviderReferenceOriginUsed = providerOriginUsed,
                    ProviderReferenceToLaunchMeters = providerToLaunch,
                    PqsSampled = pqsSampled,
                    PqsElevation = pqsSampled ? pqsElevation : 0.0
                };
                detail = "FRAME origin=" + (providerOriginUsed ? "PROVIDER_INSTANCE" :
                    "LAUNCH") + "; launchOffset=" +
                    providerToLaunch.ToString("0.0", CultureInfo.InvariantCulture) +
                    "m; launchHeading=" + launchHeading.ToString("0.00",
                        CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                detail = "RUNWAY LOCAL FRAME FAILED: " + ex.GetType().Name;
                return false;
            }
        }

        static bool RequiresAbsolutePlacementConstraint(
            AERISProviderFacilityRecord record)
        {
            if (record == null) return false;
            return record.Source == AERISAirfieldSource.KerbalKonstructs ||
                record.Source == AERISAirfieldSource.StockLaunchsitesExpansion ||
                string.Equals(record.SourceMod, "KerbalKonstructs",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourceMod, "StockLaunchsitesExpansion",
                    StringComparison.OrdinalIgnoreCase);
        }

        static bool FiniteVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        static void AddLiveGeometry(GameObject root, LocalFrame frame,
            List<AERISSurveyPoint> points, List<AERISSurveyPrimitive> primitives,
            ref int sourceGroup, ref bool colliderReadable, ref bool geometryReadable)
        {
            if (root == null) return;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length && primitives.Count < MaximumPrimitives; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null || collider.isTrigger ||
                        collider.bounds.size.sqrMagnitude < 0.01f) continue;
                    AERISSurveySemantic semantic = ClassifySemantic(collider.name,
                        collider.transform == null ? string.Empty : collider.transform.name, true);
                    if (TryAddBoundsPrimitive(collider.bounds, frame, semantic,
                        AERISRunwayEvidenceFamily.GeometryTopology,
                        AERISRunwayMeasurementMethod.M04Collider, sourceGroup++,
                        points, primitives))
                    {
                        colliderReadable = true;
                        geometryReadable = true;
                    }
                }
            }

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters != null)
            {
                for (int i = 0; i < filters.Length && primitives.Count < MaximumPrimitives; i++)
                {
                    MeshFilter filter = filters[i];
                    if (filter == null || filter.sharedMesh == null) continue;
                    Renderer renderer = filter.GetComponent<Renderer>();
                    string materialNames = MaterialNames(renderer);
                    AERISSurveySemantic semantic = ClassifySemantic(filter.name,
                        materialNames, true);
                    if (TryAddMesh(filter.sharedMesh, filter.transform.localToWorldMatrix,
                        frame, semantic, sourceGroup++, points, primitives)) geometryReadable = true;
                }
            }

            if (geometryReadable) return;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length && primitives.Count < MaximumPrimitives; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.bounds.size.sqrMagnitude < 0.01f) continue;
                AERISSurveySemantic semantic = ClassifySemantic(renderer.name,
                    MaterialNames(renderer), true);
                if (TryAddBoundsPrimitive(renderer.bounds, frame, semantic,
                    AERISRunwayEvidenceFamily.GeometryTopology,
                    AERISRunwayMeasurementMethod.M02RendererBounds, sourceGroup++,
                    points, primitives)) geometryReadable = true;
            }
        }

        static void AddPrefabGeometry(GameObject prefab, Transform instance,
            float modelScale, LocalFrame frame, List<AERISSurveyPoint> points,
            List<AERISSurveyPrimitive> primitives, ref int sourceGroup,
            ref bool geometryReadable)
        {
            if (prefab == null || instance == null) return;
            Transform prefabRoot = prefab.transform;
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters == null) return;
            float safeScale = modelScale > 0f && !float.IsNaN(modelScale) &&
                !float.IsInfinity(modelScale) ? modelScale : 1f;
            for (int i = 0; i < filters.Length && primitives.Count < MaximumPrimitives; i++)
            {
                MeshFilter filter = filters[i];
                Mesh mesh = filter == null ? null : filter.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 toRoot = prefabRoot.worldToLocalMatrix *
                    filter.transform.localToWorldMatrix;
                Matrix4x4 world = instance.localToWorldMatrix *
                    Matrix4x4.Scale(Vector3.one * safeScale) * toRoot;
                Renderer renderer = filter.GetComponent<Renderer>();
                AERISSurveySemantic semantic = ClassifySemantic(filter.name,
                    MaterialNames(renderer), true);
                if (TryAddMesh(mesh, world, frame, semantic, sourceGroup++,
                    points, primitives)) geometryReadable = true;
            }
        }

        static bool TryAddMesh(Mesh mesh, Matrix4x4 localToWorld, LocalFrame frame,
            AERISSurveySemantic semantic, int sourceGroup,
            List<AERISSurveyPoint> points, List<AERISSurveyPrimitive> primitives)
        {
            if (mesh == null || !mesh.isReadable) return false;
            Vector3[] vertices;
            try { vertices = mesh.vertices; }
            catch { return false; }
            if (vertices == null || vertices.Length < 4) return false;
            int remaining = Math.Max(0, MaximumPoints - points.Count);
            if (remaining == 0) return false;
            int step = Math.Max(1, vertices.Length / Math.Max(8, remaining));
            var local = new List<AERISSurveyPoint>(Math.Min(vertices.Length, remaining));
            for (int i = 0; i < vertices.Length && points.Count < MaximumPoints; i += step)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(vertices[i]);
                AERISSurveyPoint point;
                if (!TryLocalPoint(world, frame, semantic,
                    AERISRunwayMeasurementMethod.M03MeshPca |
                    AERISRunwayMeasurementMethod.M05SubMeshMaterial, out point)) continue;
                points.Add(point);
                local.Add(point);
            }
            AERISSurveyPrimitive primitive;
            if (!TryPrimitiveFromPoints(local, semantic,
                AERISRunwayEvidenceFamily.GeometryTopology,
                AERISRunwayMeasurementMethod.M03MeshPca |
                AERISRunwayMeasurementMethod.M05SubMeshMaterial |
                AERISRunwayMeasurementMethod.M07LongSurfaceStrip |
                ((semantic & AERISSurveySemantic.Lod) != 0
                    ? AERISRunwayMeasurementMethod.M22LodConsistency
                    : AERISRunwayMeasurementMethod.None),
                sourceGroup, out primitive)) return false;
            primitives.Add(primitive);
            return true;
        }

        static bool TryAddBoundsPrimitive(Bounds bounds, LocalFrame frame,
            AERISSurveySemantic semantic, AERISRunwayEvidenceFamily evidence,
            AERISRunwayMeasurementMethod method, int sourceGroup,
            List<AERISSurveyPoint> points, List<AERISSurveyPrimitive> primitives)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            var local = new List<AERISSurveyPoint>(8);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = new Vector3(x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                        AERISSurveyPoint point;
                        if (!TryLocalPoint(corner, frame, semantic, method, out point)) continue;
                        local.Add(point);
                        if (points.Count < MaximumPoints) points.Add(point);
                    }
            AERISSurveyPrimitive primitive;
            if (!TryPrimitiveFromPoints(local, semantic, evidence, method,
                sourceGroup, out primitive)) return false;
            primitives.Add(primitive);
            return true;
        }

        static bool TryPrimitiveFromPoints(IList<AERISSurveyPoint> points,
            AERISSurveySemantic semantic, AERISRunwayEvidenceFamily evidence,
            AERISRunwayMeasurementMethod method, int sourceGroup,
            out AERISSurveyPrimitive primitive)
        {
            primitive = new AERISSurveyPrimitive();
            if (points == null || points.Count < 4) return false;
            double meanE = 0.0;
            double meanN = 0.0;
            double meanU = 0.0;
            int count = 0;
            for (int i = 0; i < points.Count; i++)
            {
                AERISSurveyPoint point = points[i];
                if (!Finite(point.East) || !Finite(point.North) || !Finite(point.Up)) continue;
                meanE += point.East;
                meanN += point.North;
                meanU += point.Up;
                count++;
            }
            if (count < 4) return false;
            meanE /= count;
            meanN /= count;
            meanU /= count;
            double ee = 0.0;
            double nn = 0.0;
            double en = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                double e = points[i].East - meanE;
                double n = points[i].North - meanN;
                ee += e * e;
                nn += n * n;
                en += e * n;
            }
            double angle = 0.5 * Math.Atan2(2.0 * en, nn - ee);
            double axisE = Math.Sin(angle);
            double axisN = Math.Cos(angle);
            double normalE = -axisN;
            double normalN = axisE;
            double minA = double.PositiveInfinity;
            double maxA = double.NegativeInfinity;
            double minB = double.PositiveInfinity;
            double maxB = double.NegativeInfinity;
            double minU = double.PositiveInfinity;
            double maxU = double.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                AERISSurveyPoint point = points[i];
                double de = point.East - meanE;
                double dn = point.North - meanN;
                double along = de * axisE + dn * axisN;
                double across = de * normalE + dn * normalN;
                minA = Math.Min(minA, along);
                maxA = Math.Max(maxA, along);
                minB = Math.Min(minB, across);
                maxB = Math.Max(maxB, across);
                minU = Math.Min(minU, point.Up);
                maxU = Math.Max(maxU, point.Up);
            }
            double length = maxA - minA;
            double width = maxB - minB;
            if (width > length)
            {
                double temporary = length;
                length = width;
                width = temporary;
                temporary = axisE;
                axisE = normalE;
                axisN = normalN;
                normalE = temporary;
            }
            if (!Finite(length) || !Finite(width) || length < 0.1 || width < 0.1) return false;
            double height = Math.Max(0.0, maxU - minU);
            double flatness = Math.Atan2(height, Math.Max(0.1, width)) * 180.0 / Math.PI;
            if (flatness <= 3.0)
            {
                method |= AERISRunwayMeasurementMethod.M08SurfaceFlatness;
            }
            primitive = new AERISSurveyPrimitive(meanE, meanN, meanU, axisE, axisN,
                length, width, height, flatness, semantic, evidence, method, sourceGroup);
            return true;
        }

        static bool TryLocalPoint(Vector3 world, LocalFrame frame,
            AERISSurveySemantic semantic, AERISRunwayMeasurementMethod method,
            out AERISSurveyPoint point)
        {
            Vector3 delta = world - frame.Origin;
            double east = Vector3.Dot(delta, frame.East);
            double north = Vector3.Dot(delta, frame.North);
            double up = Vector3.Dot(delta, frame.Up);
            if (!Finite(east) || !Finite(north) || !Finite(up) ||
                Math.Abs(east) > CoordinateLimitMeters ||
                Math.Abs(north) > CoordinateLimitMeters ||
                Math.Abs(up) > CoordinateLimitMeters)
            {
                point = new AERISSurveyPoint();
                return false;
            }
            double weight = (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold)) != 0
                ? 1.5 : 1.0;
            point = new AERISSurveyPoint(east, north, up, weight, semantic, method);
            return true;
        }

        static AERISSurveySemantic ClassifySemantic(string name, string material,
            bool providerRunway)
        {
            string text = ((name ?? string.Empty) + " " + (material ?? string.Empty))
                .ToLowerInvariant();
            AERISSurveySemantic value = AERISSurveySemantic.None;
            if (text.Contains("taxi")) value |= AERISSurveySemantic.Taxiway;
            if (text.Contains("apron") || text.Contains("ramp")) value |= AERISSurveySemantic.Apron;
            if (text.Contains("platform") || text.Contains("foundation") ||
                text.Contains("baseplate")) value |= AERISSurveySemantic.Platform;
            if (text.Contains("blastpad") || text.Contains("blast_pad") ||
                text.Contains("blast pad") || text.Contains("overrun"))
                value |= AERISSurveySemantic.BlastPad;
            if (text.Contains("stopway") || text.Contains("stop_way") ||
                text.Contains("stop way")) value |= AERISSurveySemantic.Stopway;
            if (text.Contains("hangar") || text.Contains("hanger") ||
                text.Contains("building") || text.Contains("wall") ||
                text.Contains("tower") || text.Contains("obstacle"))
                value |= AERISSurveySemantic.Obstacle;
            if (text.Contains("lod0") || text.Contains("lod1") ||
                text.Contains("lod_0") || text.Contains("lod_1"))
                value |= AERISSurveySemantic.Lod;
            if (text.Contains("centerline") || text.Contains("centreline"))
                value |= AERISSurveySemantic.Centerline;
            if (text.Contains("threshold") || text.Contains("displaced"))
                value |= AERISSurveySemantic.Threshold;
            if (text.Contains("runwaylight") || text.Contains("edge_light") ||
                text.Contains("edgelight")) value |= AERISSurveySemantic.EdgeLight;
            if (text.Contains("approachlight") || text.Contains("alsf") ||
                text.Contains("papi")) value |= AERISSurveySemantic.ApproachLight;
            if (text.Contains("runwaynumber") || text.Contains("rwynumber") ||
                text.Contains("designation")) value |= AERISSurveySemantic.RunwayNumber;
            if (text.Contains("runway") || text.Contains("airstrip") ||
                text.Contains("landingstrip") || text.Contains("rwy"))
                value |= AERISSurveySemantic.Runway;
            if (text.Contains("spawn") || text.Contains("aircraftstart") ||
                text.Contains("aircraft_start")) value |= AERISSurveySemantic.Spawn;
            if (text.Contains("asphalt") || text.Contains("concrete") ||
                text.Contains("tarmac") || text.Contains("pavement"))
                value |= AERISSurveySemantic.Pavement;
            if (text.Contains("dirt") || text.Contains("grass") ||
                text.Contains("gravel")) value |= AERISSurveySemantic.NaturalSurface;
            bool explicitNonRunway = (value & (AERISSurveySemantic.Taxiway |
                AERISSurveySemantic.Apron | AERISSurveySemantic.Platform)) != 0;
            // The provider says the facility is a runway, but that does not make every
            // child mesh a runway.  Hangars, aprons and foundations often live under the
            // same root.  Facility metadata is fused separately by the worker.
            return value;
        }

        static bool ExplicitlyConflictsWithRunway(string category, string siteType)
        {
            string text = ((category ?? string.Empty) + " " + (siteType ?? string.Empty))
                .Trim().ToLowerInvariant();
            if (text.Length == 0) return false;
            if (text.Contains("runway") || text.Contains("airfield") ||
                text.Contains("airstrip") || text.Contains("plane")) return false;
            return text.Contains("helipad") || text.Contains("heli pad") ||
                text.Contains("harbour") || text.Contains("harbor") ||
                text.Contains("waterlaunch") || text.Contains("water launch") ||
                text.Contains("launchpad") || text.Contains("launch pad") ||
                text.Contains("rocket") || text.Contains("vab");
        }

        static string MaterialNames(Renderer renderer)
        {
            if (renderer == null) return string.Empty;
            try
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0) return string.Empty;
                var builder = new StringBuilder();
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    if (builder.Length > 0) builder.Append(' ');
                    builder.Append(materials[i].name);
                }
                return builder.ToString();
            }
            catch { return string.Empty; }
        }

        static string BuildFingerprint(AERISProviderFacilityRecord record,
            AERISRunwaySurveyDefinition definition, LocalFrame frame)
        {
            string sourceFingerprint = BuildSourceFingerprint(record, definition,
                CanonicalSourceGeometryFingerprint(record), null);
            return BuildFingerprint(record, definition, frame, sourceFingerprint, null);
        }

        static string BuildSourceFingerprint(AERISProviderFacilityRecord record,
            AERISRunwaySurveyDefinition definition, string canonicalSourceFingerprint,
            AERISRunwayWitness witness)
        {
            // This hash is the strict immutable-source gate used by the sub-metre
            // compatibility path.  It excludes provider/world placement so that a
            // floating-origin or PSystemSetup frame rebuild cannot disguise an actual
            // model/config change as harmless position jitter.
            var builder = new StringBuilder(1024);
            Append(builder, AERISRunwaySurveySnapshot.CurrentAlgorithmVersion);
            if (RequiresAbsolutePlacementConstraint(record))
            {
                Append(builder, "KK_ABSOLUTE_PLACEMENT");
                Append(builder, AERISRunwaySurveySnapshot.CurrentAbsolutePlacementRevision);
                Append(builder, "KK_RUNWAY_AXIS_REGISTRATION");
                Append(builder, AERISRunwaySurveySnapshot.CurrentAxisRegistrationRevision);
                Append(builder, "KK_MOD_AIRFIELD_RECOVERY");
                Append(builder, AERISRunwaySurveySnapshot.CurrentModAirfieldRecoveryRevision);
            }
            Append(builder, "RUNWAY_DETECTOR");
            Append(builder, AERISRunwaySurveySnapshot.CurrentRunwayDetectorRevision);
            Append(builder, StableProviderFingerprintIdentity(record));
            Append(builder, record == null ? string.Empty : record.ProviderVersion);
            Append(builder, canonicalSourceFingerprint);
            AppendSurveyDefinition(builder, definition);
            Append(builder, witness == null ? "NO_RUNWAY_WITNESS" : witness.Fingerprint);
            return Sha256Hex(builder.ToString());
        }

        static string BuildFingerprint(AERISProviderFacilityRecord record,
            AERISRunwaySurveyDefinition definition, LocalFrame frame,
            string sourceFingerprint, AERISRunwayWitness witness)
        {
            // The full exact key retains the provider placement.  A separate strict
            // source fingerprint allows a fail-safe compatibility check only when the
            // exact key differs by a measured sub-metre provider-frame reconstruction.
            var builder = new StringBuilder(1024);
            Append(builder, sourceFingerprint);
            AppendCanonicalPlacement(builder, record, frame);
            Append(builder, witness == null ? "NO_RUNWAY_WITNESS" : witness.Fingerprint);
            return Sha256Hex(builder.ToString());
        }

        static void AppendCanonicalPlacement(StringBuilder builder,
            AERISProviderFacilityRecord record, LocalFrame frame)
        {
            bool providerPosition = record != null &&
                record.ProviderReferencePositionValid && Finite(record.LatitudeDeg) &&
                Finite(record.LongitudeDeg) && Finite(record.ElevationMeters);
            double latitude = providerPosition ? record.LatitudeDeg :
                (frame == null ? 0.0 : frame.Latitude);
            double longitude = providerPosition ? record.LongitudeDeg :
                (frame == null ? 0.0 : frame.Longitude);
            double elevation = providerPosition ? record.ElevationMeters :
                (frame == null ? 0.0 : frame.Elevation);
            double heading = record == null ? 0.0 : record.OrientationHeadingDeg;
            double scale = record == null ? 1.0 : record.RuntimeModelScale;
            AppendQuantized(builder, latitude, providerPosition ? 0.00001 : 0.00005);
            AppendQuantized(builder, longitude, providerPosition ? 0.00001 : 0.00005);
            AppendQuantized(builder, elevation, providerPosition ? 0.50 : 1.00);
            AppendQuantized(builder, heading, 0.05);
            AppendQuantized(builder, scale, 0.001);
            AppendQuantized(builder, record == null ? 0.0 :
                record.DeclaredLengthMeters, 0.10);
            AppendQuantized(builder, record == null ? 0.0 :
                record.DeclaredWidthMeters, 0.10);
            Append(builder, record == null ? string.Empty : record.LaunchPadTransform);
            if (frame != null && frame.AbsolutePlacementConstraintAvailable)
            {
                AppendQuantized(builder, frame.LaunchAnchorEastMeters, 0.10);
                AppendQuantized(builder, frame.LaunchAnchorNorthMeters, 0.10);
                AppendQuantized(builder, frame.LaunchAnchorUpMeters, 0.10);
                AppendQuantized(builder, frame.LaunchAnchorHeadingDeg, 0.02);
                Append(builder, frame.ProviderReferenceOriginUsed ? 1 : 0);
            }
        }

        static void AppendSurveyDefinition(StringBuilder builder,
            AERISRunwaySurveyDefinition definition)
        {
            if (definition == null)
            {
                Append(builder, "NO_SURVEY_DEFINITION");
                return;
            }
            Append(builder, definition.Id);
            Append(builder, definition.ProviderUuid);
            Append(builder, definition.ProviderSiteId);
            Append(builder, definition.ProviderGroup);
            Append(builder, definition.SourcePathContains);
            Append(builder, definition.ModelName);
            Append(builder, (int)definition.Method);
            Append(builder, definition.PairKey);
            AppendQuantized(builder, definition.MinimumLengthMeters, 0.01);
            AppendQuantized(builder, definition.MaximumLengthMeters, 0.01);
            AppendQuantized(builder, definition.MinimumWidthMeters, 0.01);
            AppendQuantized(builder, definition.MaximumWidthMeters, 0.01);
            AppendQuantized(builder, definition.MinimumAspectRatio, 0.001);
            AppendQuantized(builder, definition.DefaultWidthMeters, 0.01);
            Append(builder, definition.Surface);
            Append(builder, definition.SourceMod);
            Append(builder, definition.ProviderVersion);
        }

        static string CanonicalSourceGeometryFingerprint(
            AERISProviderFacilityRecord record)
        {
            if (record == null) return Sha256Hex("NO_PROVIDER");
            GameObject root = record.RuntimeRunwayPrefab != null
                ? record.RuntimeRunwayPrefab : record.RuntimeRunwayObject;
            var components = new List<string>();
            if (root != null)
            {
                MeshFilter[] filters = null;
                try { filters = root.GetComponentsInChildren<MeshFilter>(true); }
                catch { filters = null; }
                if (filters != null)
                    for (int i = 0; i < filters.Length; i++)
                    {
                        string identity = MeshAssetIdentity(root.transform, filters[i]);
                        if (!string.IsNullOrEmpty(identity)) components.Add(identity);
                    }

                Collider[] colliders = null;
                try { colliders = root.GetComponentsInChildren<Collider>(true); }
                catch { colliders = null; }
                if (colliders != null)
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        string identity = ColliderAssetIdentity(root.transform,
                            colliders[i]);
                        if (!string.IsNullOrEmpty(identity)) components.Add(identity);
                    }
            }
            return CanonicalSourceGeometryFingerprint(record, components);
        }

        static string CanonicalSourceGeometryFingerprint(
            AERISProviderFacilityRecord record, IList<string> capturedComponents)
        {
            if (record == null) return Sha256Hex("NO_PROVIDER");
            string sourceKind = record.RuntimeRunwayPrefab != null ? "PREFAB" :
                (record.RuntimeRunwayObject != null ? "LIVE_ROOT" : "NO_ROOT");
            var components = new List<string>();
            if (capturedComponents != null)
                for (int i = 0; i < capturedComponents.Count; i++)
                    if (!string.IsNullOrEmpty(capturedComponents[i]))
                        components.Add(capturedComponents[i]);
            components.Sort(StringComparer.Ordinal);
            var builder = new StringBuilder(512 + components.Count * 96);
            Append(builder, sourceKind);
            Append(builder, record.ModelName);
            Append(builder, record.SourcePath);
            Append(builder, components.Count);
            for (int i = 0; i < components.Count; i++) Append(builder, components[i]);
            return Sha256Hex(builder.ToString());
        }

        static string MeshAssetIdentity(Transform root, MeshFilter filter)
        {
            if (root == null || filter == null || filter.sharedMesh == null) return string.Empty;
            Mesh mesh = filter.sharedMesh;
            var builder = new StringBuilder(256);
            Append(builder, "MESH");
            Append(builder, mesh.name);
            Append(builder, mesh.vertexCount);
            Append(builder, mesh.subMeshCount);
            Bounds bounds = mesh.bounds;
            AppendVector(builder, bounds.center, 0.001);
            AppendVector(builder, bounds.size, 0.001);
            AppendRelativeMatrix(builder, root, filter.transform);
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                try
                {
                    Append(builder, mesh.GetTopology(i));
                    Append(builder, mesh.GetIndexCount(i));
                }
                catch
                {
                    Append(builder, "SUBMESH_UNREADABLE");
                }
            }
            Renderer renderer = null;
            try { renderer = filter.GetComponent<Renderer>(); }
            catch { renderer = null; }
            Append(builder, CanonicalMaterialNames(renderer));
            Append(builder, (long)ClassifySemantic(filter.name,
                CanonicalMaterialNames(renderer), true));
            return builder.ToString();
        }

        static string ColliderAssetIdentity(Transform root, Collider collider)
        {
            if (root == null || collider == null || collider.isTrigger) return string.Empty;
            var builder = new StringBuilder(192);
            Append(builder, "COLLIDER");
            Append(builder, collider.GetType().FullName);
            AppendRelativeMatrix(builder, root, collider.transform);
            BoxCollider box = collider as BoxCollider;
            if (box != null)
            {
                AppendVector(builder, box.center, 0.001);
                AppendVector(builder, box.size, 0.001);
                return builder.ToString();
            }
            SphereCollider sphere = collider as SphereCollider;
            if (sphere != null)
            {
                AppendVector(builder, sphere.center, 0.001);
                AppendQuantized(builder, sphere.radius, 0.001);
                return builder.ToString();
            }
            CapsuleCollider capsule = collider as CapsuleCollider;
            if (capsule != null)
            {
                AppendVector(builder, capsule.center, 0.001);
                AppendQuantized(builder, capsule.radius, 0.001);
                AppendQuantized(builder, capsule.height, 0.001);
                Append(builder, capsule.direction);
                return builder.ToString();
            }
            MeshCollider meshCollider = collider as MeshCollider;
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                Mesh mesh = meshCollider.sharedMesh;
                Append(builder, mesh.name);
                Append(builder, mesh.vertexCount);
                Append(builder, mesh.subMeshCount);
                AppendVector(builder, mesh.bounds.center, 0.001);
                AppendVector(builder, mesh.bounds.size, 0.001);
                Append(builder, meshCollider.convex);
            }
            return builder.ToString();
        }

        static void AppendRelativeMatrix(StringBuilder builder, Transform root,
            Transform child)
        {
            if (root == null || child == null)
            {
                Append(builder, "NO_TRANSFORM");
                return;
            }
            try
            {
                // Do not cancel two world matrices here.  Planet-scale float
                // translation loses centimetres before the cancellation and made the
                // key dependent on floating-origin state.  Compose serialized local
                // transforms up to the canonical root instead.
                Matrix4x4 matrix = Matrix4x4.identity;
                Transform current = child;
                int depth = 0;
                while (current != null && current != root && depth < 128)
                {
                    Matrix4x4 local = Matrix4x4.TRS(current.localPosition,
                        current.localRotation, current.localScale);
                    matrix = local * matrix;
                    current = current.parent;
                    depth++;
                }
                if (current != root)
                {
                    Append(builder, "TRANSFORM_OUTSIDE_ROOT");
                    return;
                }
                for (int row = 0; row < 3; row++)
                    for (int column = 0; column < 4; column++)
                        AppendQuantized(builder, matrix[row, column], 0.001);
            }
            catch
            {
                Append(builder, "TRANSFORM_UNREADABLE");
            }
        }

        static void AppendVector(StringBuilder builder, Vector3 value,
            double quantum)
        {
            AppendQuantized(builder, value.x, quantum);
            AppendQuantized(builder, value.y, quantum);
            AppendQuantized(builder, value.z, quantum);
        }

        static string CanonicalMaterialNames(Renderer renderer)
        {
            if (renderer == null) return string.Empty;
            try
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0) return string.Empty;
                var names = new List<string>(materials.Length);
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null) names.Add(materials[i].name ?? string.Empty);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return string.Join(";", names.ToArray());
            }
            catch { return string.Empty; }
        }

        static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        static string StableProviderFingerprintIdentity(
            AERISProviderFacilityRecord record)
        {
            if (record == null) return string.Empty;
            bool hasStableProviderFields = !string.IsNullOrEmpty(record.ProviderSiteId) ||
                !string.IsNullOrEmpty(record.SourcePath) ||
                !string.IsNullOrEmpty(record.ModelName);
            string uuidFallback = hasStableProviderFields ? string.Empty :
                record.ProviderUuid ?? string.Empty;
            return (record.Body ?? string.Empty) + "|" + record.Source.ToString() + "|" +
                record.FacilityKind.ToString() + "|" +
                (record.ProviderSiteId ?? string.Empty) + "|" +
                (record.ProviderGroup ?? string.Empty) + "|" +
                (record.SourcePath ?? string.Empty) + "|" +
                (record.ModelName ?? string.Empty) + "|" + uuidFallback;
        }

        static long QuantizedFingerprintValue(double value, double quantum)
        {
            if (!Finite(value) || quantum <= 0.0) return long.MinValue;
            double scaled = Math.Round(value / quantum, MidpointRounding.AwayFromZero);
            if (scaled >= long.MaxValue) return long.MaxValue;
            if (scaled <= long.MinValue) return long.MinValue;
            return (long)scaled;
        }

        static void AppendQuantized(StringBuilder builder, double value,
            double quantum)
        {
            Append(builder, QuantizedFingerprintValue(value, quantum));
        }

        static string StableRecordId(AERISProviderFacilityRecord record)
        {
            return AERISProviderIdentity.StableRecordId(record);
        }

        static void Append(StringBuilder builder, object value)
        {
            if (value is double)
                builder.Append(((double)value).ToString("0.######", CultureInfo.InvariantCulture));
            else if (value is float)
                builder.Append(((float)value).ToString("0.######", CultureInfo.InvariantCulture));
            else builder.Append(value == null ? string.Empty : Convert.ToString(value,
                CultureInfo.InvariantCulture));
            builder.Append('|');
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
