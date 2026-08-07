using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // Operation Health Step 3. This partial owns only pure projection work and the
    // main-thread handoff around it. Worker execution never calls Unity/KSP objects:
    // no Mesh, Material, RenderTexture, Transform, Vessel, CelestialBody, Graphics or GL.
    // UnityEngine.Vector3 is used only as a value-array payload; native Mesh upload remains
    // exclusively on the renderer main thread.
    internal sealed partial class AERISTerrainGpuTileRenderer
    {
        const string ProjectionWorkerJobKey = "nd-terrain-exact-projection";
        const float ProjectionWorkerMinimumCommitIntervalSeconds = 0.10f;

        sealed class ProjectionWorkerBuffers
        {
            internal Vector3[] Land;
            internal Vector3[] Water;
            internal Vector3[] CoastalLand;
            internal Vector3[] CoastalWater;
            internal Vector3[] Contour;
            internal Vector3[] Coastline;
        }

        sealed class ProjectionSourceSet
        {
            internal GeographicUnitPoint[] LandSource;
            internal Vector3[] LandDestination;
            internal GeographicUnitPoint[] WaterSource;
            internal Vector3[] WaterDestination;
            internal GeographicUnitPoint[] CoastalLandSource;
            internal Vector3[] CoastalLandDestination;
            internal GeographicUnitPoint[] CoastalWaterSource;
            internal Vector3[] CoastalWaterDestination;
            internal GeographicUnitPoint[] ContourSource;
            internal Vector3[] ContourDestination;
            internal GeographicUnitPoint[] CoastlineSource;
            internal Vector3[] CoastlineDestination;
        }

        sealed class ProjectionWorkerRequest
        {
            internal long Serial;
            internal long BaseFrontBufferSwaps;
            internal long TerrainGeneration;
            internal long ViewGeneration;
            internal long ContentRevision;
            internal AERISNdMapProjection Projection;
            internal double CenterLatitudeDeg;
            internal double CenterLongitudeDeg;
            internal float RangeMeters;
            internal float MapHeadingDeg;
            internal bool TrackUp;
            internal float AnchorV;
            internal AERISTerrainRenderTargetOrientation Orientation;
            internal AERISTerrainDisplayMode EffectiveMode;
            internal AERISTerrainColourPreset ColourPreset;
            internal string BodyName;
            internal long BodyRadiusMillimetres;
            internal int EntryCount;
            internal Entry[] Entries;
            internal ProjectionWorkerBuffers[] Buffers;
            internal ProjectionSourceSet[] Sources;
        }

        sealed class ProjectionWorkerResult
        {
            internal ProjectionWorkerRequest Request;
            internal long VertexCount;
            internal double WorkerMilliseconds;
        }

        readonly ConditionalWeakTable<Entry, ProjectionWorkerBuffers>
            projectionWorkerBuffers =
                new ConditionalWeakTable<Entry, ProjectionWorkerBuffers>();
        Entry[] projectionWorkerEntrySnapshot = new Entry[0];
        ProjectionWorkerBuffers[] projectionWorkerBufferSnapshot =
            new ProjectionWorkerBuffers[0];
        ProjectionSourceSet[] projectionWorkerSourceSnapshot =
            new ProjectionSourceSet[0];
        bool projectionWorkerPending;
        ProjectionWorkerResult projectionWorkerCompleted;
        long projectionWorkerSerial;
        long operationHealthProjectionWorkerSubmits;
        long operationHealthProjectionWorkerCommits;
        long operationHealthProjectionWorkerFallbacks;
        long operationHealthProjectionWorkerStale;
        long operationHealthProjectionWorkerFailures;
        long operationHealthProjectionWorkerVertices;
        long operationHealthProjectionWorkerCommitDeferrals;
        double lastProjectionWorkerMilliseconds;

        bool ProjectionWorkerEligible(bool contentTickRequired,
            bool forceCenterProjectionRefresh, bool colourRefreshRequired,
            AERISTerrainVisibleTileSet visible, int readyFar)
        {
            if (!forceCenterProjectionRefresh || contentTickRequired ||
                colourRefreshRequired || !frontBufferValid || !requestedViewReady ||
                !contentSnapshotValid || visible == null || !visible.FoundationComplete)
                return false;
            if (lastBackFoundationCoverage < 0.999f ||
                readyFar < visible.FarFoundationCount) return false;
            if (frontTerrainGeneration != visible.TerrainGeneration ||
                frontViewGeneration != visible.ViewGeneration ||
                frontContentRevision != gpuContentRevision) return false;
            return true;
        }

        bool TrySubmitProjectionWorker(AERISTerrainVisibleTileSet visible,
            AERISNdMapProjection projection, AERISTerrainDisplayMode effectiveMode,
            AERISTerrainColourPreset colourPreset, Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (projectionWorkerPending || projectionWorkerCompleted != null ||
                visible == null || vessel == null || vessel.mainBody == null ||
                drawEntriesScratch == null || drawEntriesScratch.Length == 0)
                return false;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return false;

            EnsureProjectionWorkerSnapshotCapacity(drawEntriesScratch.Length);
            for (int i = 0; i < drawEntriesScratch.Length; i++)
            {
                Entry entry = drawEntriesScratch[i];
                projectionWorkerEntrySnapshot[i] = entry;
                if (entry == null)
                {
                    projectionWorkerBufferSnapshot[i] = null;
                    ClearProjectionSourceSet(projectionWorkerSourceSnapshot[i]);
                    continue;
                }
                ProjectionWorkerBuffers buffers = EnsureProjectionWorkerBuffers(entry);
                projectionWorkerBufferSnapshot[i] = buffers;
                BindProjectionSourceSet(projectionWorkerSourceSnapshot[i], entry, buffers);
            }

            var request = new ProjectionWorkerRequest
            {
                Serial = ++projectionWorkerSerial,
                BaseFrontBufferSwaps = frontBufferSwaps,
                TerrainGeneration = visible.TerrainGeneration,
                ViewGeneration = visible.ViewGeneration,
                ContentRevision = gpuContentRevision,
                Projection = projection,
                CenterLatitudeDeg = centerLatitudeDeg,
                CenterLongitudeDeg = centerLongitudeDeg,
                RangeMeters = rangeMeters,
                MapHeadingDeg = mapHeadingDeg,
                TrackUp = trackUp,
                AnchorV = anchorV,
                Orientation = orientation,
                EffectiveMode = effectiveMode,
                ColourPreset = colourPreset,
                BodyName = vessel.mainBody.name ?? string.Empty,
                BodyRadiusMillimetres = (long)Math.Round(
                    Math.Max(0.0, vessel.mainBody.Radius) * 1000.0),
                EntryCount = drawEntriesScratch.Length,
                Entries = projectionWorkerEntrySnapshot,
                Buffers = projectionWorkerBufferSnapshot,
                Sources = projectionWorkerSourceSnapshot
            };

            projectionWorkerPending = true;
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute, ProjectionWorkerJobKey,
                runtime.CaptureStamp(), context =>
                {
                    context.ThrowIfStale();
                    ProjectionWorkerResult result = BuildProjectionWorkerResult(
                        request, context);
                    context.ThrowIfStale();
                    return result;
                }, value => CompleteProjectionWorker(request, value));
            if (!accepted)
            {
                projectionWorkerPending = false;
                return false;
            }
            operationHealthProjectionWorkerSubmits++;
            return true;
        }

        static ProjectionWorkerResult BuildProjectionWorkerResult(
            ProjectionWorkerRequest request, AERISRuntimeJobContext context)
        {
            long start = Stopwatch.GetTimestamp();
            long vertices = 0L;
            if (request != null && request.Sources != null)
            {
                int count = Math.Min(request.EntryCount, request.Sources.Length);
                for (int i = 0; i < count; i++)
                {
                    context.ThrowIfStale();
                    ProjectionSourceSet source = request.Sources[i];
                    if (source == null) continue;
                    vertices += ProjectWorkerPoints(source.LandSource,
                        source.LandDestination, request.Projection);
                    vertices += ProjectWorkerPoints(source.WaterSource,
                        source.WaterDestination, request.Projection);
                    vertices += ProjectWorkerPoints(source.CoastalLandSource,
                        source.CoastalLandDestination, request.Projection);
                    vertices += ProjectWorkerPoints(source.CoastalWaterSource,
                        source.CoastalWaterDestination, request.Projection);
                    vertices += ProjectWorkerPoints(source.ContourSource,
                        source.ContourDestination, request.Projection);
                    vertices += ProjectWorkerPoints(source.CoastlineSource,
                        source.CoastlineDestination, request.Projection);
                }
            }
            return new ProjectionWorkerResult
            {
                Request = request,
                VertexCount = vertices,
                WorkerMilliseconds = (Stopwatch.GetTimestamp() - start) *
                    1000.0 / Stopwatch.Frequency
            };
        }

        // Pure worker math only. ProjectUnitToRenderNUp itself is pure double/float math;
        // Vector3 construction is a value write and does not enter the Unity native API.
        static long ProjectWorkerPoints(GeographicUnitPoint[] points,
            Vector3[] destination, AERISNdMapProjection projection)
        {
            if (points == null || destination == null ||
                points.Length != destination.Length) return 0L;
            for (int i = 0; i < points.Length; i++)
            {
                GeographicUnitPoint point = points[i];
                float u, v;
                projection.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                    out u, out v);
                destination[i] = new Vector3(u, v, 0f);
            }
            return points.LongLength;
        }

        void CompleteProjectionWorker(ProjectionWorkerRequest request, object value)
        {
            // Called by the central scheduler's main-thread commit drain. Keep this tiny:
            // native Mesh upload/rendering happens later inside Draw(), never under the
            // scheduler commit lock.
            projectionWorkerPending = false;
            if (disposed)
            {
                projectionWorkerCompleted = null;
                return;
            }
            ProjectionWorkerResult result = value as ProjectionWorkerResult;
            if (result == null || request == null || result.Request == null ||
                result.Request.Serial != request.Serial)
            {
                operationHealthProjectionWorkerFailures++;
                projectionWorkerCompleted = null;
                return;
            }
            projectionWorkerCompleted = result;
        }

        bool TryCommitProjectionWorkerResult(Rect plot, Vessel vessel,
            AERISNdMapLockReference lockReference)
        {
            ProjectionWorkerResult result = projectionWorkerCompleted;
            if (result == null) return false;
            // Worker completion latency may vary. Never allow that jitter to turn the
            // fixed 10 Hz authoritative source into two closely-spaced FRONT commits.
            // Keep the completed result intact until the absolute 0.10 s FRONT gate opens.
            if (frontBufferValid && Time.realtimeSinceStartup - frontCommittedRealtime <
                ProjectionWorkerMinimumCommitIntervalSeconds)
            {
                operationHealthProjectionWorkerCommitDeferrals++;
                return false;
            }

            projectionWorkerCompleted = null;
            ProjectionWorkerRequest request = result.Request;
            if (!ProjectionWorkerResultStillCurrent(request, vessel) ||
                !ProjectionWorkerBuffersMatchCurrentEntries(request))
            {
                operationHealthProjectionWorkerStale++;
                return false;
            }

            Matrix4x4 mapRotation = request.Projection.ResolveScaleCorrectedRenderMatrix();
            float runwayError = MeasureRunwayMapLockError(plot, request.Projection,
                mapRotation, lockReference);
            if (runwayError > 1.0f)
            {
                operationHealthProjectionWorkerStale++;
                return false;
            }
            lastRunwayMapLockErrorPixels = runwayError;

            // Validate every mesh/buffer pair before changing any native Mesh. This makes
            // worker presentation atomic with respect to content replacement.
            for (int i = 0; i < request.EntryCount; i++)
            {
                Entry entry = request.Entries[i];
                ProjectionWorkerBuffers buffers = request.Buffers[i];
                if (entry == null) continue;
                if (!ProjectionWorkerBuffersMatch(entry, buffers))
                {
                    operationHealthProjectionWorkerStale++;
                    return false;
                }
            }

            for (int i = 0; i < request.EntryCount; i++)
            {
                Entry entry = request.Entries[i];
                ProjectionWorkerBuffers buffers = request.Buffers[i];
                if (entry == null || buffers == null) continue;
                ApplyProjectionWorkerMesh(entry.LandMesh, buffers.Land);
                ApplyProjectionWorkerMesh(entry.WaterMesh, buffers.Water);
                ApplyProjectionWorkerMesh(entry.CoastalLandCorrectionMesh,
                    buffers.CoastalLand);
                ApplyProjectionWorkerMesh(entry.CoastalWaterCorrectionMesh,
                    buffers.CoastalWater);
                ApplyProjectionWorkerMesh(entry.ContourMesh, buffers.Contour);
                ApplyProjectionWorkerMesh(entry.CoastlineMesh, buffers.Coastline);
                entry.LastProjectionCenterLatitudeDeg = request.CenterLatitudeDeg;
                entry.LastProjectionCenterLongitudeDeg = request.CenterLongitudeDeg;
                entry.LastProjectionBodyRadius = request.Projection.RadiusMeters;
                entry.LastProjectionRangeMeters =
                    (float)request.Projection.VerticalMeters;
                entry.LastProjectionAnchorBottom = request.Projection.AnchorRenderV;
                entry.LastProjectionOrientation = request.Projection.Orientation;
            }

            // Preserve Hotfix 3 telemetry semantics: this frame performed the exact
            // moving-center projection, but the arithmetic happened on the worker.
            operationHealthForcedProjectionRefreshes++;
            bool rendered = RenderBackBuffer(sortedTilesScratch, drawEntriesScratch,
                request.Projection, mapRotation, request.EffectiveMode, vessel,
                request.RangeMeters, false);
            backRenderFrames++;
            lastBackAttemptViewGeneration = request.ViewGeneration;
            lastBackAttemptContentRevision = request.ContentRevision;
            nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime;
            if (!rendered || contentVisible == null ||
                !contentVisible.FoundationComplete ||
                lastBackFoundationCoverage < 0.999f ||
                contentReadyFar < contentVisible.FarFoundationCount)
            {
                blockedIncompleteSwaps++;
                operationHealthProjectionWorkerStale++;
                return false;
            }

            SwapFrontAndBack(contentVisible, vessel, request.CenterLatitudeDeg,
                request.CenterLongitudeDeg, request.RangeMeters, request.RangeMeters,
                request.MapHeadingDeg, request.TrackUp, request.AnchorV,
                request.Orientation);
            frontColourMode = request.EffectiveMode;
            frontColourPreset = request.ColourPreset;
            if (contentGpuReadyPending)
            {
                MarkVisibleGpuReady(sortedTilesScratch);
                contentGpuReadyPending = false;
            }
            PresentFrontDirect(plot, frontOrientation);
            directFrontFrames++;
            lastHistoryReprojected = false;
            lastHistoryConfidence = 0f;
            lastFrontBufferPresented = true;
            lastFrontBufferLatched = false;
            CapturePresentedProjection(false);
            lastVisualCoverageFraction = 1f;
            RecordPresentedFrontAlignmentDiagnostic(plot, sortedTilesScratch, vessel,
                request.EffectiveMode, request.ColourPreset, lockReference);
            lastDrawState = requestedViewReady ? AERISTerrainGpuDrawState.Complete :
                AERISTerrainGpuDrawState.Partial;
            operationHealthProjectionWorkerCommits++;
            operationHealthProjectionWorkerVertices += result.VertexCount;
            lastProjectionWorkerMilliseconds = result.WorkerMilliseconds;
            return true;
        }

        bool ProjectionWorkerResultStillCurrent(ProjectionWorkerRequest request,
            Vessel vessel)
        {
            if (request == null || disposed || gpuFailed || vessel == null ||
                vessel.mainBody == null || backTarget == null || frontTarget == null ||
                !backTarget.IsCreated() || !frontTarget.IsCreated() ||
                !contentSnapshotValid || contentVisible == null ||
                request.BaseFrontBufferSwaps != frontBufferSwaps ||
                request.ContentRevision != gpuContentRevision ||
                request.TerrainGeneration != contentVisible.TerrainGeneration ||
                request.ViewGeneration != contentVisible.ViewGeneration ||
                request.EntryCount != drawEntriesScratch.Length ||
                request.Entries == null || request.Buffers == null ||
                request.Entries.Length < request.EntryCount ||
                request.Buffers.Length < request.EntryCount)
                return false;
            if (!string.Equals(request.BodyName, vessel.mainBody.name,
                    StringComparison.OrdinalIgnoreCase) ||
                request.BodyRadiusMillimetres != (long)Math.Round(
                    Math.Max(0.0, vessel.mainBody.Radius) * 1000.0)) return false;
            AERISTerrainColourPreset currentPreset = settings == null ?
                AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;
            AERISTerrainDisplayMode requestedMode = settings == null ?
                AERISTerrainDisplayMode.Automatic : settings.TerrainDisplayMode;
            AERISTerrainDisplayMode effectiveNow = ResolveEffectiveMode(requestedMode,
                vessel, request.RangeMeters);
            if (currentPreset != request.ColourPreset ||
                effectiveNow != request.EffectiveMode) return false;
            return true;
        }

        bool ProjectionWorkerBuffersMatchCurrentEntries(ProjectionWorkerRequest request)
        {
            if (request == null || request.Entries == null ||
                request.EntryCount != drawEntriesScratch.Length) return false;
            for (int i = 0; i < request.EntryCount; i++)
                if (!ReferenceEquals(request.Entries[i], drawEntriesScratch[i]))
                    return false;
            return true;
        }

        static bool ProjectionWorkerBuffersMatch(Entry entry,
            ProjectionWorkerBuffers buffers)
        {
            if (entry == null) return true;
            if (buffers == null) return false;
            return ProjectionWorkerMeshMatches(entry.LandMesh,
                       entry.LandGeographicPoints, buffers.Land) &&
                ProjectionWorkerMeshMatches(entry.WaterMesh,
                    entry.WaterGeographicPoints, buffers.Water) &&
                ProjectionWorkerMeshMatches(entry.CoastalLandCorrectionMesh,
                    entry.CoastalLandCorrectionGeographicPoints,
                    buffers.CoastalLand) &&
                ProjectionWorkerMeshMatches(entry.CoastalWaterCorrectionMesh,
                    entry.CoastalWaterCorrectionGeographicPoints,
                    buffers.CoastalWater) &&
                ProjectionWorkerMeshMatches(entry.ContourMesh,
                    entry.ContourGeographicPoints, buffers.Contour) &&
                ProjectionWorkerMeshMatches(entry.CoastlineMesh,
                    entry.CoastlineGeographicPoints, buffers.Coastline);
        }

        static bool ProjectionWorkerMeshMatches(Mesh mesh,
            GeographicUnitPoint[] source, Vector3[] buffer)
        {
            if (mesh == null) return source == null || source.Length == 0;
            return source != null && buffer != null &&
                mesh.vertexCount == source.Length && source.Length == buffer.Length;
        }

        void ApplyProjectionWorkerMesh(Mesh mesh, Vector3[] vertices)
        {
            if (mesh == null) return;
            mesh.vertices = vertices;
            operationHealthBoundsSkips++;
        }

        void EnsureProjectionWorkerSnapshotCapacity(int count)
        {
            count = Math.Max(0, count);
            if (projectionWorkerEntrySnapshot.Length == count &&
                projectionWorkerBufferSnapshot.Length == count &&
                projectionWorkerSourceSnapshot.Length == count) return;
            projectionWorkerEntrySnapshot = new Entry[count];
            projectionWorkerBufferSnapshot = new ProjectionWorkerBuffers[count];
            projectionWorkerSourceSnapshot = new ProjectionSourceSet[count];
            for (int i = 0; i < count; i++)
                projectionWorkerSourceSnapshot[i] = new ProjectionSourceSet();
        }

        ProjectionWorkerBuffers EnsureProjectionWorkerBuffers(Entry entry)
        {
            ProjectionWorkerBuffers buffers = projectionWorkerBuffers.GetValue(entry,
                key => new ProjectionWorkerBuffers());
            buffers.Land = EnsureProjectionWorkerArray(buffers.Land,
                entry.LandGeographicPoints);
            buffers.Water = EnsureProjectionWorkerArray(buffers.Water,
                entry.WaterGeographicPoints);
            buffers.CoastalLand = EnsureProjectionWorkerArray(buffers.CoastalLand,
                entry.CoastalLandCorrectionGeographicPoints);
            buffers.CoastalWater = EnsureProjectionWorkerArray(buffers.CoastalWater,
                entry.CoastalWaterCorrectionGeographicPoints);
            buffers.Contour = EnsureProjectionWorkerArray(buffers.Contour,
                entry.ContourGeographicPoints);
            buffers.Coastline = EnsureProjectionWorkerArray(buffers.Coastline,
                entry.CoastlineGeographicPoints);
            return buffers;
        }

        static Vector3[] EnsureProjectionWorkerArray(Vector3[] existing,
            GeographicUnitPoint[] source)
        {
            if (source == null || source.Length == 0) return null;
            return existing != null && existing.Length == source.Length ? existing :
                new Vector3[source.Length];
        }

        static void BindProjectionSourceSet(ProjectionSourceSet set, Entry entry,
            ProjectionWorkerBuffers buffers)
        {
            if (set == null || entry == null || buffers == null) return;
            set.LandSource = entry.LandGeographicPoints;
            set.LandDestination = buffers.Land;
            set.WaterSource = entry.WaterGeographicPoints;
            set.WaterDestination = buffers.Water;
            set.CoastalLandSource = entry.CoastalLandCorrectionGeographicPoints;
            set.CoastalLandDestination = buffers.CoastalLand;
            set.CoastalWaterSource = entry.CoastalWaterCorrectionGeographicPoints;
            set.CoastalWaterDestination = buffers.CoastalWater;
            set.ContourSource = entry.ContourGeographicPoints;
            set.ContourDestination = buffers.Contour;
            set.CoastlineSource = entry.CoastlineGeographicPoints;
            set.CoastlineDestination = buffers.Coastline;
        }

        static void ClearProjectionSourceSet(ProjectionSourceSet set)
        {
            if (set == null) return;
            set.LandSource = null; set.LandDestination = null;
            set.WaterSource = null; set.WaterDestination = null;
            set.CoastalLandSource = null; set.CoastalLandDestination = null;
            set.CoastalWaterSource = null; set.CoastalWaterDestination = null;
            set.ContourSource = null; set.ContourDestination = null;
            set.CoastlineSource = null; set.CoastlineDestination = null;
        }

        string ProjectionWorkerTelemetryText()
        {
            return "; oh_project_worker_submit=" + operationHealthProjectionWorkerSubmits +
                "; oh_project_worker_commit=" + operationHealthProjectionWorkerCommits +
                "; oh_project_worker_fallback=" + operationHealthProjectionWorkerFallbacks +
                "; oh_project_worker_stale=" + operationHealthProjectionWorkerStale +
                "; oh_project_worker_fail=" + operationHealthProjectionWorkerFailures +
                "; oh_project_worker_vertices=" + operationHealthProjectionWorkerVertices +
                "; oh_project_worker_defer=" + operationHealthProjectionWorkerCommitDeferrals +
                "; project_worker_ms=" + lastProjectionWorkerMilliseconds.ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; project_worker_pending=" +
                    (projectionWorkerPending ? "1" : "0");
        }
    }
}
