using System;
using System.Collections.Generic;
using System.Diagnostics;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    internal sealed class AERISTerrainGpuTileRasterRequest
    {
        internal int Generation;
        internal AERISTerrainHeightTile Tile;
        internal bool ContoursEnabled;
        internal bool ShadingEnabled;
        internal float ContourIntervalMeters;
        internal string StyleKey;
        internal AERISTerrainVirtualDetailProfile VirtualDetailProfile;
    }

    // Gate 4A CPU-authoritative render-ready height field. This immutable payload is
    // produced on the bounded GeneralCompute lane and may remain resident after every
    // Unity Mesh/RenderTexture has been released. It contains no UnityEngine.Object.
    internal class AERISTerrainRenderReadyHeightField
    {
        internal int Generation;
        internal AERISTerrainTileKey Key;
        internal long TileCreatedUtcTicks;
        internal string StyleKey;
        internal int Resolution;
        internal double SouthLatitudeDeg;
        internal double NorthLatitudeDeg;
        internal double WestLongitudeDeg;
        internal double EastLongitudeDeg;
        internal float[] VertexX;
        internal float[] VertexY;
        internal float[] ElevationMeters;
        internal byte[] Water;
        internal byte[] Valid;
        internal byte[] Shade;
        internal int[] Triangles;
        internal float[] ContourSegments;
        internal float[] CoastlineSegments;
        internal int CoastlineResolution;
        // Candidate8 sparse coastal correction: only high-density sub-cells that belong
        // to coarse parent cells crossed by the 129x129 boundary are emitted. The base
        // terrain mesh remains 33x33/17x17. XY arrays are triangle-list vertices.
        internal float[] CoastalLandCorrectionVertices;
        internal float[] CoastalLandCorrectionElevationMeters;
        internal byte[] CoastalLandCorrectionShade;
        internal float[] CoastalWaterCorrectionVertices;
        internal int CoastalCorrectionParentCells;
        internal float MeshMilliseconds;
        internal float ContourMilliseconds;
        internal float WorkerMilliseconds;
        internal AERISTerrainVirtualDetailLevel VirtualDetailLevel;
        internal long LastUseSequence;
        internal bool ResidentTokenValid;
        internal AERISResidentCommitToken ResidentToken;

        internal long EstimatedBytes
        {
            get
            {
                long bytes = 384L;
                bytes += VertexX == null ? 0L : VertexX.LongLength * sizeof(float);
                bytes += VertexY == null ? 0L : VertexY.LongLength * sizeof(float);
                bytes += ElevationMeters == null ? 0L : ElevationMeters.LongLength * sizeof(float);
                bytes += Water == null ? 0L : Water.LongLength;
                bytes += Valid == null ? 0L : Valid.LongLength;
                bytes += Shade == null ? 0L : Shade.LongLength;
                bytes += Triangles == null ? 0L : Triangles.LongLength * sizeof(int);
                bytes += ContourSegments == null ? 0L : ContourSegments.LongLength * sizeof(float);
                bytes += CoastlineSegments == null ? 0L : CoastlineSegments.LongLength * sizeof(float);
                bytes += CoastalLandCorrectionVertices == null ? 0L :
                    CoastalLandCorrectionVertices.LongLength * sizeof(float);
                bytes += CoastalLandCorrectionElevationMeters == null ? 0L :
                    CoastalLandCorrectionElevationMeters.LongLength * sizeof(float);
                bytes += CoastalLandCorrectionShade == null ? 0L :
                    CoastalLandCorrectionShade.LongLength;
                bytes += CoastalWaterCorrectionVertices == null ? 0L :
                    CoastalWaterCorrectionVertices.LongLength * sizeof(float);
                return Math.Max(0L, bytes);
            }
        }
    }

    // Compatibility result name retained for the established bounded worker queue.
    internal sealed class AERISTerrainGpuTileRasterResult :
        AERISTerrainRenderReadyHeightField
    {
    }

    // Pure-data GPU mesh preparation. Height values remain explicit metres across the
    // worker boundary. The Unity main thread converts them to final TOPO/REL vertex
    // colours without relying on built-in-shader texture scale/offset behaviour. The
    // worker computes immutable topology, fixed-NW slope factors, land-only contours
    // and a dedicated coastline polyline on the bounded GeneralCompute lane.
    internal sealed class AERISTerrainGpuTileRasterizer : IDisposable
    {
        // Hard runtime safety rail: a pathological coastline must never expand the
        // sparse correction back toward Candidate7's whole-tile 129x129 cost.
        const int MaximumSparseCorrectionParentCells = 64;
        sealed class PendingState
        {
            internal int Generation;
            internal long CreatedUtcTicks;
            internal string StyleKey;
            internal string SchedulerKey;
            internal long EnqueuedTicks;
        }

        readonly object gate = new object();
        readonly Dictionary<string, PendingState> pending =
            new Dictionary<string, PendingState>(StringComparer.Ordinal);
        readonly Queue<AERISTerrainGpuTileRasterResult> completed =
            new Queue<AERISTerrainGpuTileRasterResult>();
        bool disposed;
        int dropped;
        int failures;

        internal int PendingCount { get { lock (gate) return pending.Count; } }
        internal int DroppedCount { get { lock (gate) return dropped; } }
        internal int FailureCount { get { lock (gate) return failures; } }

        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)
        {
            if (disposed || request == null || request.Tile == null ||
                request.Tile.Elevation == null || request.Tile.Flags == null ||
                string.IsNullOrEmpty(request.StyleKey)) return false;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return false;
            string tileId = request.Tile.Key.StableId;
            long createdUtcTicks = request.Tile.CreatedUtcTicks;
            lock (gate)
            {
                PendingState existing;
                if (pending.TryGetValue(tileId, out existing) && existing != null &&
                    existing.CreatedUtcTicks == createdUtcTicks &&
                    string.Equals(existing.StyleKey, request.StyleKey, StringComparison.Ordinal) &&
                    ElapsedSeconds(existing.EnqueuedTicks) < 10.0) return true;
                string pendingSchedulerKey = "terrain-gpu-mesh:" +
                    request.Tile.Key.FileStem;
                pending[tileId] = new PendingState
                {
                    Generation = request.Generation,
                    CreatedUtcTicks = createdUtcTicks,
                    StyleKey = request.StyleKey,
                    SchedulerKey = pendingSchedulerKey,
                    EnqueuedTicks = Stopwatch.GetTimestamp()
                };
            }
            request.Tile = request.Tile.CloneImmutable();
            string schedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
            bool accepted = runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute,
                schedulerKey, runtime.CaptureStamp(), context =>
                {
                    context.ThrowIfStale();
                    AERISTerrainGpuTileRasterResult result = BuildMesh(request);
                    context.ThrowIfStale();
                    return result;
                }, value =>
                {
                    AERISTerrainGpuTileRasterResult result =
                        value as AERISTerrainGpuTileRasterResult;
                    lock (gate)
                    {
                        if (disposed) return;
                        PendingState newest;
                        if (!pending.TryGetValue(tileId, out newest) || newest == null ||
                            newest.Generation != request.Generation)
                        {
                            dropped++;
                            return;
                        }
                        pending.Remove(tileId);
                        if (result == null)
                        {
                            failures++;
                            return;
                        }
                        while (completed.Count >= 64)
                        {
                            completed.Dequeue();
                            dropped++;
                        }
                        completed.Enqueue(result);
                    }
                });
            if (!accepted)
            {
                lock (gate)
                {
                    PendingState newest;
                    if (pending.TryGetValue(tileId, out newest) && newest != null &&
                        newest.Generation == request.Generation) pending.Remove(tileId);
                    dropped++;
                }
            }
            return accepted;
        }

        internal void CancelAll()
        {
            var schedulerKeys = new List<string>();
            lock (gate)
            {
                foreach (PendingState state in pending.Values)
                    if (state != null && !string.IsNullOrEmpty(state.SchedulerKey))
                        schedulerKeys.Add(state.SchedulerKey);
                pending.Clear();
                completed.Clear();
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            for (int i = 0; i < schedulerKeys.Count; i++)
                runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,
                    schedulerKeys[i]);
        }

        internal int Drain(List<AERISTerrainGpuTileRasterResult> destination, int maximum)
        {
            if (destination == null || maximum <= 0) return 0;
            int count = 0;
            lock (gate)
            {
                while (count < maximum && completed.Count > 0)
                {
                    destination.Add(completed.Dequeue());
                    count++;
                }
            }
            return count;
        }

        static AERISTerrainGpuTileRasterResult BuildMesh(
            AERISTerrainGpuTileRasterRequest request)
        {
            AERISTerrainHeightTile sourceTile = request.Tile;
            AERISTerrainHeightTile tile = AERISTerrainVirtualDetailPolicy.ReconstructFar(
                sourceTile, request.VirtualDetailProfile);
            int resolution = tile == null ? 0 : tile.Resolution;
            if (resolution < 2 || resolution > 257) return null;
            int count = resolution * resolution;
            if (tile.Elevation.Length < count || tile.Flags.Length < count) return null;

            bool highDensityBoundary =
                AERISTerrainCoastlineExtractor.HasCurrentHighDensityPayload(tile);

            Stopwatch watch = Stopwatch.StartNew();
            var x = new float[count];
            var y = new float[count];
            var elevationMeters = new float[count];
            var water = new byte[count];
            var valid = new byte[count];
            var shade = new byte[count];
            // Candidate8 restores the Candidate5 33x33/17x17 base mesh. High-density
            // coastal data is applied later as a narrow correction overlay, never as a
            // full 129x129 surface mesh.
            double finalIntervals = Math.Max(1,
                AERISTerrainTileFormat.Resolution(tile.Key.Lod) - 1);
            double actualIntervals = Math.Max(1, resolution - 1);
            float cellMeters = (float)Math.Max(1.0,
                AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod) *
                finalIntervals / actualIntervals);
            for (int row = 0; row < resolution; row++)
            {
                for (int column = 0; column < resolution; column++)
                {
                    int index = row * resolution + column;
                    x[index] = column / (float)(resolution - 1);
                    y[index] = row / (float)(resolution - 1);
                    float value = tile.Elevation[index];
                    bool isValid = tile.Flags[index] != 0 && Finite(value);
                    valid[index] = isValid ? (byte)255 : (byte)0;
                    water[index] = tile.Flags[index] == 2 ? (byte)1 : (byte)0;
                    elevationMeters[index] = isValid ? value : 0f;
                    shade[index] = isValid && request.ShadingEnabled && water[index] == 0 ?
                        ResolveShade(tile, row, column, value, cellMeters) : (byte)255;
                }
            }

            var triangles = new List<int>((resolution - 1) * (resolution - 1) * 6);
            for (int row = 0; row < resolution - 1; row++)
            {
                for (int column = 0; column < resolution - 1; column++)
                {
                    int a = row * resolution + column;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    if (valid[a] != 0 && valid[b] != 0 && valid[c] != 0)
                    { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
                    if (valid[b] != 0 && valid[c] != 0 && valid[d] != 0)
                    { triangles.Add(b); triangles.Add(c); triangles.Add(d); }
                }
            }

            float[] correctionLandXY = new float[0];
            float[] correctionLandElevation = new float[0];
            byte[] correctionLandShade = new byte[0];
            float[] correctionWaterXY = new float[0];
            int correctionParents = 0;
            if (highDensityBoundary)
                BuildSparseCoastalCorrections(tile, request.ShadingEnabled,
                    out correctionLandXY, out correctionLandElevation,
                    out correctionLandShade, out correctionWaterXY,
                    out correctionParents);

            float meshMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            Stopwatch contourWatch = Stopwatch.StartNew();
            float[] contours = request.ContoursEnabled ?
                BuildContours(tile, Math.Max(25f, request.ContourIntervalMeters)) :
                new float[0];
            float[] coastlines = highDensityBoundary ?
                (float[])tile.HighDensityCoastlineSegments.Clone() :
                AERISTerrainCoastlineExtractor.Build(tile);
            contourWatch.Stop();
            watch.Stop();
            return new AERISTerrainGpuTileRasterResult
            {
                Generation = request.Generation,
                Key = tile.Key,
                TileCreatedUtcTicks = tile.CreatedUtcTicks,
                StyleKey = request.StyleKey,
                Resolution = resolution,
                SouthLatitudeDeg = tile.SouthLatitudeDeg,
                NorthLatitudeDeg = tile.NorthLatitudeDeg,
                WestLongitudeDeg = tile.WestLongitudeDeg,
                EastLongitudeDeg = tile.EastLongitudeDeg,
                VertexX = x,
                VertexY = y,
                ElevationMeters = elevationMeters,
                Water = water,
                Valid = valid,
                Shade = shade,
                Triangles = triangles.ToArray(),
                ContourSegments = contours,
                CoastlineSegments = coastlines,
                CoastlineResolution = highDensityBoundary ?
                    tile.HighDensityCoastlineResolution : tile.Resolution,
                CoastalLandCorrectionVertices = correctionLandXY,
                CoastalLandCorrectionElevationMeters = correctionLandElevation,
                CoastalLandCorrectionShade = correctionLandShade,
                CoastalWaterCorrectionVertices = correctionWaterXY,
                CoastalCorrectionParentCells = correctionParents,
                MeshMilliseconds = meshMilliseconds,
                ContourMilliseconds = (float)contourWatch.Elapsed.TotalMilliseconds,
                WorkerMilliseconds = (float)watch.Elapsed.TotalMilliseconds,
                VirtualDetailLevel = request.VirtualDetailProfile == null ?
                    AERISTerrainVirtualDetailLevel.FarDirect :
                    request.VirtualDetailProfile.Level
            };
        }

        struct CorrectionPoint
        {
            internal float X;
            internal float Y;
            internal byte ClassFlag;
            internal float Elevation;
        }

        static void BuildSparseCoastalCorrections(AERISTerrainHeightTile tile,
            bool shadingEnabled, out float[] landXY, out float[] landElevation,
            out byte[] landShade, out float[] waterXY, out int parentCellCount)
        {
            landXY = new float[0];
            landElevation = new float[0];
            landShade = new byte[0];
            waterXY = new float[0];
            parentCellCount = 0;
            if (tile == null || tile.HighDensityCoastalFlags == null ||
                tile.HighDensityCoastlineResolution !=
                    AERISTerrainCoastlineExtractor.HighDensityResolution)
                return;
            int hd = tile.HighDensityCoastlineResolution;
            int baseResolution = tile.Resolution;
            if (baseResolution < 2 || hd < 2 ||
                (hd - 1) % (baseResolution - 1) != 0) return;
            int factor = (hd - 1) / (baseResolution - 1);
            if (factor <= 0) return;
            byte[] flags = tile.HighDensityCoastalFlags;
            if (flags.Length != hd * hd) return;

            int parentWidth = baseResolution - 1;
            var parents = new bool[parentWidth * parentWidth];
            for (int row = 0; row < hd - 1; row++)
            {
                for (int column = 0; column < hd - 1; column++)
                {
                    int a = row * hd + column;
                    int b = a + 1;
                    int c = a + hd;
                    int d = c + 1;
                    byte fa = flags[a], fb = flags[b], fc = flags[c], fd = flags[d];
                    if (fa == 0 || fb == 0 || fc == 0 || fd == 0) continue;
                    bool wa = fa == 2, wb = fb == 2, wc = fc == 2, wd = fd == 2;
                    if (wa == wb && wa == wc && wa == wd) continue;
                    int pr = Math.Min(parentWidth - 1, row / factor);
                    int pc = Math.Min(parentWidth - 1, column / factor);
                    parents[pr * parentWidth + pc] = true;
                }
            }

            int detectedParents = 0;
            for (int i = 0; i < parents.Length; i++)
                if (parents[i]) detectedParents++;
            if (detectedParents <= 0 ||
                detectedParents > MaximumSparseCorrectionParentCells)
                return;

            var land = new List<float>(2048);
            var water = new List<float>(2048);
            var landHeights = new List<float>(1024);
            var landShades = new List<byte>(1024);
            float baseCellMeters = (float)Math.Max(1.0,
                AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod));
            for (int pr = 0; pr < parentWidth; pr++)
            {
                for (int pc = 0; pc < parentWidth; pc++)
                {
                    if (!parents[pr * parentWidth + pc]) continue;
                    parentCellCount++;
                    int rowStart = pr * factor;
                    int columnStart = pc * factor;
                    for (int sr = 0; sr < factor; sr++)
                    {
                        int row = rowStart + sr;
                        for (int sc = 0; sc < factor; sc++)
                        {
                            int column = columnStart + sc;
                            CorrectionPoint a = CorrectionSample(tile, flags, hd,
                                row, column);
                            CorrectionPoint b = CorrectionSample(tile, flags, hd,
                                row, column + 1);
                            CorrectionPoint c = CorrectionSample(tile, flags, hd,
                                row + 1, column);
                            CorrectionPoint d = CorrectionSample(tile, flags, hd,
                                row + 1, column + 1);
                            if (a.ClassFlag == 0 || b.ClassFlag == 0 ||
                                c.ClassFlag == 0 || d.ClassFlag == 0) continue;
                            AppendCorrectionTriangle(tile, land, landHeights, landShades,
                                water, a, c, b, shadingEnabled, baseCellMeters);
                            AppendCorrectionTriangle(tile, land, landHeights, landShades,
                                water, b, c, d, shadingEnabled, baseCellMeters);
                        }
                    }
                }
            }
            landXY = land.ToArray();
            landElevation = landHeights.ToArray();
            landShade = landShades.ToArray();
            waterXY = water.ToArray();
        }

        static CorrectionPoint CorrectionSample(AERISTerrainHeightTile tile,
            byte[] flags, int resolution, int row, int column)
        {
            int index = row * resolution + column;
            byte classFlag = flags[index];
            float x = column / (float)(resolution - 1);
            float y = row / (float)(resolution - 1);
            return new CorrectionPoint
            {
                X = x,
                Y = y,
                ClassFlag = classFlag,
                Elevation = classFlag == 0 ? 0f :
                    SampleClassPreservingHeight(tile, x, y, classFlag)
            };
        }

        static void AppendCorrectionTriangle(AERISTerrainHeightTile tile,
            List<float> land, List<float> landHeights, List<byte> landShades,
            List<float> water, CorrectionPoint a, CorrectionPoint b,
            CorrectionPoint c, bool shadingEnabled, float baseCellMeters)
        {
            var input = new CorrectionPoint[] { a, b, c };
            AppendCorrectionPolygon(tile, land, landHeights, landShades, input,
                false, shadingEnabled, baseCellMeters);
            AppendCorrectionPolygon(tile, water, null, null, input,
                true, false, baseCellMeters);
        }

        static void AppendCorrectionPolygon(AERISTerrainHeightTile tile,
            List<float> output, List<float> elevations, List<byte> shades,
            CorrectionPoint[] input, bool targetWater, bool shadingEnabled,
            float baseCellMeters)
        {
            var clipped = new CorrectionPoint[6];
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                CorrectionPoint current = input[i];
                CorrectionPoint next = input[(i + 1) % 3];
                bool currentInside = (current.ClassFlag == 2) == targetWater;
                bool nextInside = (next.ClassFlag == 2) == targetWater;
                if (currentInside) clipped[count++] = current;
                if (currentInside != nextInside)
                    clipped[count++] = CorrectionCrossing(current, next,
                        targetWater ? (byte)2 : (byte)1);
            }
            if (count < 3) return;
            for (int i = 1; i < count - 1; i++)
            {
                AppendCorrectionVertex(tile, output, elevations, shades, clipped[0],
                    targetWater, shadingEnabled, baseCellMeters);
                AppendCorrectionVertex(tile, output, elevations, shades, clipped[i],
                    targetWater, shadingEnabled, baseCellMeters);
                AppendCorrectionVertex(tile, output, elevations, shades, clipped[i + 1],
                    targetWater, shadingEnabled, baseCellMeters);
            }
        }

        static CorrectionPoint CorrectionCrossing(CorrectionPoint a,
            CorrectionPoint b, byte targetClass)
        {
            float t = AERISTerrainCoastlinePolicy.CrossingFraction(a.ClassFlag == 2,
                b.ClassFlag == 2, a.Elevation, b.Elevation);
            return new CorrectionPoint
            {
                X = a.X + (b.X - a.X) * t,
                Y = a.Y + (b.Y - a.Y) * t,
                ClassFlag = targetClass,
                Elevation = a.Elevation + (b.Elevation - a.Elevation) * t
            };
        }

        static void AppendCorrectionVertex(AERISTerrainHeightTile tile,
            List<float> output, List<float> elevations, List<byte> shades,
            CorrectionPoint point, bool targetWater, bool shadingEnabled,
            float baseCellMeters)
        {
            output.Add(point.X);
            output.Add(point.Y);
            if (targetWater || elevations == null || shades == null) return;
            float elevation = SampleClassPreservingHeight(tile, point.X, point.Y, 1);
            elevations.Add(elevation);
            shades.Add(shadingEnabled ? ResolveShadeAtNormalized(tile, point.X, point.Y,
                elevation, baseCellMeters) : (byte)255);
        }

        static byte ResolveShadeAtNormalized(AERISTerrainHeightTile tile,
            float normalizedX, float normalizedY, float fallback, float cellMeters)
        {
            if (tile == null || tile.Resolution < 2) return 255;
            float step = 1f / Math.Max(1, tile.Resolution - 1);
            float west = SampleClassPreservingHeight(tile,
                Math.Max(0f, normalizedX - step), normalizedY, 1);
            float east = SampleClassPreservingHeight(tile,
                Math.Min(1f, normalizedX + step), normalizedY, 1);
            float south = SampleClassPreservingHeight(tile, normalizedX,
                Math.Max(0f, normalizedY - step), 1);
            float north = SampleClassPreservingHeight(tile, normalizedX,
                Math.Min(1f, normalizedY + step), 1);
            if (!Finite(west)) west = fallback;
            if (!Finite(east)) east = fallback;
            if (!Finite(south)) south = fallback;
            if (!Finite(north)) north = fallback;
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f);
            float ny = -(north - south) / Math.Max(2f, cellMeters * 2f);
            float nz = 1f;
            float inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inverse; ny *= inverse; nz *= inverse;
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f);
            float factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f);
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        static float SampleClassPreservingHeight(AERISTerrainHeightTile tile,
            float normalizedX, float normalizedY, byte classFlag)
        {
            if (tile == null || tile.Resolution < 2 || tile.Elevation == null ||
                tile.Flags == null || classFlag == 0) return 0f;
            double sx = Math.Max(0.0, Math.Min(tile.Resolution - 1.0,
                normalizedX * (tile.Resolution - 1.0)));
            double sy = Math.Max(0.0, Math.Min(tile.Resolution - 1.0,
                normalizedY * (tile.Resolution - 1.0)));
            int x0 = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Floor(sx)));
            int y0 = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Floor(sy)));
            int x1 = Math.Min(tile.Resolution - 1, x0 + 1);
            int y1 = Math.Min(tile.Resolution - 1, y0 + 1);
            double fx = sx - x0;
            double fy = sy - y0;
            bool targetWater = classFlag == 2;
            double sum = 0.0, weight = 0.0;
            AccumulateClassHeight(tile, y0 * tile.Resolution + x0,
                (1.0 - fx) * (1.0 - fy), targetWater, ref sum, ref weight);
            AccumulateClassHeight(tile, y0 * tile.Resolution + x1,
                fx * (1.0 - fy), targetWater, ref sum, ref weight);
            AccumulateClassHeight(tile, y1 * tile.Resolution + x0,
                (1.0 - fx) * fy, targetWater, ref sum, ref weight);
            AccumulateClassHeight(tile, y1 * tile.Resolution + x1,
                fx * fy, targetWater, ref sum, ref weight);
            if (weight > 0.000001) return (float)(sum / weight);

            int cx = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Round(sx)));
            int cy = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Round(sy)));
            double bestDistance = double.MaxValue;
            float best = 0f;
            bool found = false;
            for (int radius = 0; radius <= 2; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx, py = cy + dy;
                        if (px < 0 || py < 0 || px >= tile.Resolution ||
                            py >= tile.Resolution) continue;
                        int index = py * tile.Resolution + px;
                        if (tile.Flags[index] == 0 ||
                            (tile.Flags[index] == 2) != targetWater ||
                            !Finite(tile.Elevation[index])) continue;
                        double distance = dx * dx + dy * dy;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = tile.Elevation[index];
                        found = true;
                    }
                if (found) return best;
            }

            int fallback = cy * tile.Resolution + cx;
            return fallback >= 0 && fallback < tile.Elevation.Length &&
                Finite(tile.Elevation[fallback]) ? tile.Elevation[fallback] : 0f;
        }

        static void AccumulateClassHeight(AERISTerrainHeightTile tile, int index,
            double sampleWeight, bool targetWater, ref double sum, ref double weight)
        {
            if (sampleWeight <= 0.0 || index < 0 || index >= tile.Elevation.Length ||
                index >= tile.Flags.Length || tile.Flags[index] == 0 ||
                (tile.Flags[index] == 2) != targetWater ||
                !Finite(tile.Elevation[index])) return;
            sum += tile.Elevation[index] * sampleWeight;
            weight += sampleWeight;
        }

        static byte ResolveShadeGrid(float[] elevation, byte[] valid, int resolution,
            int row, int column, float fallback, float cellMeters)
        {
            int westIndex = row * resolution + Math.Max(0, column - 1);
            int eastIndex = row * resolution + Math.Min(resolution - 1, column + 1);
            int southIndex = Math.Max(0, row - 1) * resolution + column;
            int northIndex = Math.Min(resolution - 1, row + 1) * resolution + column;
            float west = valid[westIndex] != 0 ? elevation[westIndex] : fallback;
            float east = valid[eastIndex] != 0 ? elevation[eastIndex] : fallback;
            float south = valid[southIndex] != 0 ? elevation[southIndex] : fallback;
            float north = valid[northIndex] != 0 ? elevation[northIndex] : fallback;
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f);
            float ny = -(north - south) / Math.Max(2f, cellMeters * 2f);
            float nz = 1f;
            float inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inverse; ny *= inverse; nz *= inverse;
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f);
            float factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f);
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        static byte ResolveShade(AERISTerrainHeightTile tile, int row, int column,
            float fallback, float cellMeters)
        {
            float west = Sample(tile, row, Math.Max(0, column - 1), fallback);
            float east = Sample(tile, row, Math.Min(tile.Resolution - 1, column + 1), fallback);
            float south = Sample(tile, Math.Max(0, row - 1), column, fallback);
            float north = Sample(tile, Math.Min(tile.Resolution - 1, row + 1), column, fallback);
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f);
            float ny = -(north - south) / Math.Max(2f, cellMeters * 2f);
            float nz = 1f;
            float inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inverse; ny *= inverse; nz *= inverse;
            // Fixed north-west light. It is independent of sun, time of day and aircraft.
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f);
            float factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f);
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        static float[] BuildContours(AERISTerrainHeightTile tile, float interval)
        {
            var output = new List<float>(tile.Resolution * tile.Resolution);
            var points = new float[8];
            int resolution = tile.Resolution;
            for (int row = 0; row < resolution - 1; row++)
            {
                for (int column = 0; column < resolution - 1; column++)
                {
                    int a = row * resolution + column;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    if (tile.Flags[a] == 0 || tile.Flags[b] == 0 ||
                        tile.Flags[c] == 0 || tile.Flags[d] == 0 ||
                        tile.Flags[a] == 2 || tile.Flags[b] == 2 ||
                        tile.Flags[c] == 2 || tile.Flags[d] == 2) continue;
                    float va = tile.Elevation[a], vb = tile.Elevation[b];
                    float vc = tile.Elevation[c], vd = tile.Elevation[d];
                    if (!Finite(va) || !Finite(vb) || !Finite(vc) || !Finite(vd)) continue;
                    float minimum = Math.Min(Math.Min(va, vb), Math.Min(vc, vd));
                    float maximum = Math.Max(Math.Max(va, vb), Math.Max(vc, vd));
                    int first = (int)Math.Floor(minimum / interval) + 1;
                    int last = (int)Math.Floor(maximum / interval);
                    int levels = Math.Min(16, Math.Max(0, last - first + 1));
                    for (int levelIndex = 0; levelIndex < levels; levelIndex++)
                    {
                        float level = (first + levelIndex) * interval;
                        int pointCount = 0;
                        AddCrossing(points, ref pointCount, column, row, column + 1, row,
                            va, vb, level, resolution);
                        AddCrossing(points, ref pointCount, column + 1, row, column + 1, row + 1,
                            vb, vd, level, resolution);
                        AddCrossing(points, ref pointCount, column + 1, row + 1, column, row + 1,
                            vd, vc, level, resolution);
                        AddCrossing(points, ref pointCount, column, row + 1, column, row,
                            vc, va, level, resolution);
                        if (pointCount >= 2)
                        {
                            output.Add(points[0]); output.Add(points[1]);
                            output.Add(points[2]); output.Add(points[3]);
                        }
                        if (pointCount >= 4)
                        {
                            output.Add(points[4]); output.Add(points[5]);
                            output.Add(points[6]); output.Add(points[7]);
                        }
                    }
                }
            }
            return output.ToArray();
        }


        static void AddCrossing(float[] points, ref int pointCount,
            int x0, int y0, int x1, int y1, float v0, float v1, float level,
            int resolution)
        {
            if (pointCount >= 4 || v0 == v1 ||
                !((v0 <= level && v1 > level) || (v1 <= level && v0 > level))) return;
            float t = (level - v0) / (v1 - v0);
            points[pointCount * 2] = (x0 + (x1 - x0) * t) / (resolution - 1f);
            points[pointCount * 2 + 1] = (y0 + (y1 - y0) * t) / (resolution - 1f);
            pointCount++;
        }

        static float Sample(AERISTerrainHeightTile tile, int row, int column,
            float fallback)
        {
            int index = row * tile.Resolution + column;
            return index >= 0 && index < tile.Elevation.Length && tile.Flags[index] != 0 &&
                Finite(tile.Elevation[index]) ? tile.Elevation[index] : fallback;
        }

        static double ElapsedSeconds(long startTicks)
        {
            if (startTicks <= 0L) return double.PositiveInfinity;
            return (Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency;
        }

        static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (gate)
            {
                pending.Clear();
                completed.Clear();
            }
        }
    }
}
