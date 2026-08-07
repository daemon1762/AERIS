using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // Operation Health Step 3. Worker execution receives only immutable projection
    // snapshots, geographic unit-point arrays and plain float output arrays. All native
    // Unity/KSP work (Mesh upload, render, FRONT swap and Vessel/body validation) stays on
    // the main thread.
    internal sealed partial class AERISTerrainGpuTileRenderer
    {
        const string ProjectionWorkerJobKey = "nd-terrain-exact-projection";
        const float ProjectionWorkerMinimumCommitIntervalSeconds = 0.10f;
        const float ProjectionWorkerTimeoutSeconds = 0.095f;

        sealed class ProjectionPlaneBuffer
        {
            internal float[] U;
            internal float[] V;
        }

        sealed class ProjectionWorkerBuffers
        {
            internal ProjectionPlaneBuffer Land;
            internal ProjectionPlaneBuffer Water;
            internal ProjectionPlaneBuffer CoastalLand;
            internal ProjectionPlaneBuffer CoastalWater;
            internal ProjectionPlaneBuffer Contour;
            internal ProjectionPlaneBuffer Coastline;
        }

        sealed class ProjectionSourceSet
        {
            internal GeographicUnitPoint[] LandSource;
            internal ProjectionPlaneBuffer LandDestination;
            internal GeographicUnitPoint[] WaterSource;
            internal ProjectionPlaneBuffer WaterDestination;
            internal GeographicUnitPoint[] CoastalLandSource;
            internal ProjectionPlaneBuffer CoastalLandDestination;
            internal GeographicUnitPoint[] CoastalWaterSource;
            internal ProjectionPlaneBuffer CoastalWaterDestination;
            internal GeographicUnitPoint[] ContourSource;
            internal ProjectionPlaneBuffer ContourDestination;
            internal GeographicUnitPoint[] CoastlineSource;
            internal ProjectionPlaneBuffer CoastlineDestination;
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
        readonly HashSet<long> projectionWorkerTimeoutCancelledSerials =
            new HashSet<long>();
        bool projectionWorkerPending;
        long projectionWorkerPendingSerial = -1L;
        float projectionWorkerSubmittedRealtime = -1f;
        long projectionWorkerLastDeferredSerial = -1L;
        ProjectionWorkerResult projectionWorkerCompleted;
        long projectionWorkerSerial;
        long operationHealthProjectionWorkerSubmits;
        long operationHealthProjectionWorkerCommits;
        long operationHealthProjectionWorkerFallbacks;
        long operationHealthProjectionWorkerStale;
        long operationHealthProjectionWorkerFailures;
        long operationHealthProjectionWorkerVertices;
        long operationHealthProjectionWorkerCommitDeferrals;
        long operationHealthProjectionWorkerWaitHolds;
        long operationHealthProjectionWorkerTimeoutFallbacks;
        long operationHealthProjectionWorkerBufferBytes;
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
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;

            // A healthy in-flight/completed worker owns this authoritative refresh.
            // Returning true here means "worker path accepted/holds the refresh", not
            // necessarily "a new job was submitted". This prevents the caller from
            // performing the old exact main-thread projection at the same time.
            if (projectionWorkerCompleted != null)
            {
                operationHealthProjectionWorkerWaitHolds++;
                return true;
            }
            if (projectionWorkerPending)
            {
                float now = Time.realtimeSinceStartup;
                float pendingAge = projectionWorkerSubmittedRealtime < 0f ? 0f :
                    Math.Max(0f, now - projectionWorkerSubmittedRealtime);
                bool frontCommitGateOpen = !frontBufferValid ||
                    now - frontCommittedRealtime >=
                        ProjectionWorkerMinimumCommitIntervalSeconds;

                // Do not duplicate healthy worker work. A timeout may fall back only
                // after the previous FRONT has satisfied the same 0.10 s authority, so
                // Worker -> main-thread recovery can never create a >10 Hz FRONT burst.
                if (pendingAge < ProjectionWorkerTimeoutSeconds ||
                    !frontCommitGateOpen || runtime == null || runtime.Scheduler == null)
                {
                    operationHealthProjectionWorkerWaitHolds++;
                    return true;
                }

                long cancelledSerial = projectionWorkerPendingSerial;
                if (cancelledSerial >= 0L)
                    projectionWorkerTimeoutCancelledSerials.Add(cancelledSerial);
                runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,
                    ProjectionWorkerJobKey);
                projectionWorkerPending = false;
                projectionWorkerPendingSerial = -1L;
                projectionWorkerSubmittedRealtime = -1f;
                projectionWorkerCompleted = null;
                operationHealthProjectionWorkerTimeoutFallbacks++;
                return false;
            }

            if (visible == null || vessel == null || vessel.mainBody == null ||
                drawEntriesScratch == null || drawEntriesScratch.Length == 0)
                return false;
            if (runtime == null || runtime.Scheduler == null) return false;

            EnsureProjectionWorkerSnapshotCapacity(drawEntriesScratch.Length);
            long bufferBytes = 0L;
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
                bufferBytes += ProjectionWorkerBufferBytes(buffers);
            }
            operationHealthProjectionWorkerBufferBytes = bufferBytes;

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
            projectionWorkerPendingSerial = request.Serial;
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
                projectionWorkerPendingSerial = -1L;
                projectionWorkerSubmittedRealtime = -1f;
                return false;
            }
            projectionWorkerSubmittedRealtime = Time.realtimeSinceStartup;
            operationHealthProjectionWorkerSubmits++;
            return true;
        }

        // Pure worker section. No UnityEngine or KSP object is read or written here.
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

        static long ProjectWorkerPoints(GeographicUnitPoint[] points,
            ProjectionPlaneBuffer destination, AERISNdMapProjection projection)
        {
            if (points == null || destination == null || destination.U == null ||
                destination.V == null || points.Length != destination.U.Length ||
                points.Length != destination.V.Length) return 0L;
            for (int i = 0; i < points.Length; i++)
            {
                GeographicUnitPoint point = points[i];
                float u, v;
                projection.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                    out u, out v);
                destination.U[i] = u;
                destination.V[i] = v;
            }
            return points.LongLength;
        }

        void CompleteProjectionWorker(ProjectionWorkerRequest request, object value)
        {
            // Scheduler drains this on the main thread under its own commit lock. Do not
            // render or touch native Unity state here. Serial ownership prevents an old
            // timeout-cancelled callback from clearing a newer worker request.
            long requestSerial = request == null ? -1L : request.Serial;
            bool timeoutCancelled = requestSerial >= 0L &&
                projectionWorkerTimeoutCancelledSerials.Remove(requestSerial);
            if (projectionWorkerPendingSerial == requestSerial)
            {
                projectionWorkerPending = false;
                projectionWorkerPendingSerial = -1L;
                projectionWorkerSubmittedRealtime = -1f;
            }
            if (disposed)
            {
                projectionWorkerCompleted = null;
                return;
            }
            if (timeoutCancelled)
            {
                // CancelKey intentionally terminates this request; it is not a worker
                // failure and must not overwrite a newer completed result.
                return;
            }
            if (requestSerial != projectionWorkerSerial)
            {
                operationHealthProjectionWorkerStale++;
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
            // Completion latency is asynchronous, but FRONT authority is not. Keep a
            // completed result until at least 0.10 s has elapsed since the prior FRONT.
            if (frontBufferValid && Time.realtimeSinceStartup - frontCommittedRealtime <
                ProjectionWorkerMinimumCommitIntervalSeconds)
            {
                long serial = result.Request == null ? -1L : result.Request.Serial;
                if (projectionWorkerLastDeferredSerial != serial)
                {
                    projectionWorkerLastDeferredSerial = serial;
                    operationHealthProjectionWorkerCommitDeferrals++;
                }
                return false;
            }

            projectionWorkerLastDeferredSerial = -1L;
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

            // Validate the whole presentation set before changing a single native Mesh.
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

            // Main thread performs only float -> Vector3 packing and native Mesh upload.
            for (int i = 0; i < request.EntryCount; i++)
            {
                Entry entry = request.Entries[i];
                ProjectionWorkerBuffers buffers = request.Buffers[i];
                if (entry == null || buffers == null) continue;
                ApplyProjectionWorkerMesh(entry.LandMesh,
                    entry.LandProjectedVertices, buffers.Land);
                ApplyProjectionWorkerMesh(entry.WaterMesh,
                    entry.WaterProjectedVertices, buffers.Water);
                ApplyProjectionWorkerMesh(entry.CoastalLandCorrectionMesh,
                    entry.CoastalLandCorrectionProjectedVertices, buffers.CoastalLand);
                ApplyProjectionWorkerMesh(entry.CoastalWaterCorrectionMesh,
                    entry.CoastalWaterCorrectionProjectedVertices, buffers.CoastalWater);
                ApplyProjectionWorkerMesh(entry.ContourMesh,
                    entry.ContourProjectedVertices, buffers.Contour);
                ApplyProjectionWorkerMesh(entry.CoastlineMesh,
                    entry.CoastlineProjectedVertices, buffers.Coastline);
                entry.LastProjectionCenterLatitudeDeg = request.CenterLatitudeDeg;
                entry.LastProjectionCenterLongitudeDeg = request.CenterLongitudeDeg;
                entry.LastProjectionBodyRadius = request.Projection.RadiusMeters;
                entry.LastProjectionRangeMeters =
                    (float)request.Projection.VerticalMeters;
                entry.LastProjectionAnchorBottom = request.Projection.AnchorRenderV;
                entry.LastProjectionOrientation = request.Projection.Orientation;
            }

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
                       entry.LandGeographicPoints, entry.LandProjectedVertices,
                       buffers.Land) &&
                ProjectionWorkerMeshMatches(entry.WaterMesh,
                    entry.WaterGeographicPoints, entry.WaterProjectedVertices,
                    buffers.Water) &&
                ProjectionWorkerMeshMatches(entry.CoastalLandCorrectionMesh,
                    entry.CoastalLandCorrectionGeographicPoints,
                    entry.CoastalLandCorrectionProjectedVertices,
                    buffers.CoastalLand) &&
                ProjectionWorkerMeshMatches(entry.CoastalWaterCorrectionMesh,
                    entry.CoastalWaterCorrectionGeographicPoints,
                    entry.CoastalWaterCorrectionProjectedVertices,
                    buffers.CoastalWater) &&
                ProjectionWorkerMeshMatches(entry.ContourMesh,
                    entry.ContourGeographicPoints, entry.ContourProjectedVertices,
                    buffers.Contour) &&
                ProjectionWorkerMeshMatches(entry.CoastlineMesh,
                    entry.CoastlineGeographicPoints, entry.CoastlineProjectedVertices,
                    buffers.Coastline);
        }

        static bool ProjectionWorkerMeshMatches(Mesh mesh,
            GeographicUnitPoint[] source, Vector3[] mainThreadVertices,
            ProjectionPlaneBuffer buffer)
        {
            if (mesh == null)
                return source == null || source.Length == 0;
            return source != null && mainThreadVertices != null && buffer != null &&
                buffer.U != null && buffer.V != null &&
                mesh.vertexCount == source.Length &&
                mainThreadVertices.Length == source.Length &&
                buffer.U.Length == source.Length && buffer.V.Length == source.Length;
        }

        void ApplyProjectionWorkerMesh(Mesh mesh, Vector3[] mainThreadVertices,
            ProjectionPlaneBuffer buffer)
        {
            if (mesh == null || mainThreadVertices == null || buffer == null) return;
            for (int i = 0; i < mainThreadVertices.Length; i++)
                mainThreadVertices[i] = new Vector3(buffer.U[i], buffer.V[i], 0f);
            mesh.vertices = mainThreadVertices;
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
            buffers.Land = EnsureProjectionWorkerPlane(buffers.Land,
                entry.LandGeographicPoints);
            buffers.Water = EnsureProjectionWorkerPlane(buffers.Water,
                entry.WaterGeographicPoints);
            buffers.CoastalLand = EnsureProjectionWorkerPlane(buffers.CoastalLand,
                entry.CoastalLandCorrectionGeographicPoints);
            buffers.CoastalWater = EnsureProjectionWorkerPlane(buffers.CoastalWater,
                entry.CoastalWaterCorrectionGeographicPoints);
            buffers.Contour = EnsureProjectionWorkerPlane(buffers.Contour,
                entry.ContourGeographicPoints);
            buffers.Coastline = EnsureProjectionWorkerPlane(buffers.Coastline,
                entry.CoastlineGeographicPoints);
            return buffers;
        }

        static ProjectionPlaneBuffer EnsureProjectionWorkerPlane(
            ProjectionPlaneBuffer existing, GeographicUnitPoint[] source)
        {
            if (source == null || source.Length == 0) return null;
            ProjectionPlaneBuffer output = existing ?? new ProjectionPlaneBuffer();
            if (output.U == null || output.U.Length != source.Length)
                output.U = new float[source.Length];
            if (output.V == null || output.V.Length != source.Length)
                output.V = new float[source.Length];
            return output;
        }

        static long ProjectionWorkerBufferBytes(ProjectionWorkerBuffers buffers)
        {
            if (buffers == null) return 0L;
            return ProjectionPlaneBytes(buffers.Land) +
                ProjectionPlaneBytes(buffers.Water) +
                ProjectionPlaneBytes(buffers.CoastalLand) +
                ProjectionPlaneBytes(buffers.CoastalWater) +
                ProjectionPlaneBytes(buffers.Contour) +
                ProjectionPlaneBytes(buffers.Coastline);
        }

        static long ProjectionPlaneBytes(ProjectionPlaneBuffer buffer)
        {
            if (buffer == null) return 0L;
            return ((buffer.U == null ? 0L : buffer.U.LongLength) +
                (buffer.V == null ? 0L : buffer.V.LongLength)) * sizeof(float);
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
                "; oh_project_worker_wait_hold=" + operationHealthProjectionWorkerWaitHolds +
                "; oh_project_worker_timeout=" + operationHealthProjectionWorkerTimeoutFallbacks +
                "; project_worker_buffer_bytes=" + operationHealthProjectionWorkerBufferBytes +
                "; project_worker_ms=" + lastProjectionWorkerMilliseconds.ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; project_worker_pending=" +
                    (projectionWorkerPending ? "1" : "0") +
                "; project_worker_pending_age_ms=" +
                    (projectionWorkerPending && projectionWorkerSubmittedRealtime >= 0f ?
                        Math.Max(0f, (Time.realtimeSinceStartup -
                            projectionWorkerSubmittedRealtime) * 1000f).ToString("F1",
                            CultureInfo.InvariantCulture) : "0.0") +
                "; project_worker_handoff_hf=1";
        }
    }
}
