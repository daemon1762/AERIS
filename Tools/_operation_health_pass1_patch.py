#!/usr/bin/env python3
from pathlib import Path
p=Path('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs')
s=p.read_text(encoding='utf-8')

def one(old,new,label):
    global s
    n=s.count(old)
    if n!=1:
        raise SystemExit(f'{label}: expected 1 match, found {n}')
    s=s.replace(old,new,1)

one(
'''        readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainRenderReadyHeightField>''',
'''        readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        // Operation Health Pass 1: entry selection is keyed by immutable TileKey.
        // This preserves the exact Candidate11 current/fallback selection rules while
        // eliminating repeated scans over unrelated GPU entries every repaint.
        readonly Dictionary<AERISTerrainTileKey, List<Entry>> entriesByTile =
            new Dictionary<AERISTerrainTileKey, List<Entry>>();
        readonly Dictionary<string, AERISTerrainRenderReadyHeightField>''',
'entry index field')

one(
'''        readonly HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        readonly List<CoverageRegion> coverageRects =''',
'''        readonly HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        // Pending markers used to be represented as cacheKey + "|PENDING", allocating a
        // second string per scheduled tile. Keep scheduling identity in its own set.
        readonly HashSet<string> scheduledThisFrame =
            new HashSet<string>(StringComparer.Ordinal);
        readonly List<CoverageRegion> coverageRects =''',
'schedule set field')

one(
'''        readonly List<Entry> supersededScratch = new List<Entry>(16);
        long useSequence;''',
'''        readonly List<Entry> supersededScratch = new List<Entry>(16);
        // Reusable exact-length presentation scratch. No visual or ordering authority is
        // changed; this only removes Clone()/temporary entry lookup churn on Repaint.
        AERISTerrainHeightTile[] sortedTilesScratch = new AERISTerrainHeightTile[0];
        Entry[] fallbackEntriesScratch = new Entry[0];
        Entry[] currentEntriesScratch = new Entry[0];
        Entry[] drawEntriesScratch = new Entry[0];
        long operationHealthResolveCalls;
        long operationHealthResolveCandidates;
        long operationHealthTileScratchResizes;
        long operationHealthPreparedEntryUses;
        long useSequence;''',
'scratch fields')

one(
'''            DrainCompleted(system);
            requested.Clear();

            AERISTerrainHeightTile[] tiles = (AERISTerrainHeightTile[])visible.Tiles.Clone();
            Array.Sort(tiles, CompareTilesCoarseFirst);
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null) continue;
                string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                requested.Add(cacheKey);
                Entry fallbackEntry, currentEntry;
                ResolveRenderableEntries(tile, styleKey, out fallbackEntry,
                    out currentEntry);
                if (currentEntry == null)
                {
                    if (!TryUploadRenderReadyField(tile, styleKey, system,
                        out currentEntry))
                        Schedule(tile, styleKey, contourInterval, virtualDetail);
                }
                if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                if (currentEntry != null) currentEntry.LastUse = ++useSequence;
            }
''',
'''            DrainCompleted(system);
            requested.Clear();
            scheduledThisFrame.Clear();

            AERISTerrainHeightTile[] tiles = PrepareSortedTileScratch(visible.Tiles);
            EnsureEntryScratch(tiles == null ? 0 : tiles.Length);
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null)
                {
                    fallbackEntriesScratch[i] = null;
                    currentEntriesScratch[i] = null;
                    drawEntriesScratch[i] = null;
                    continue;
                }
                // CacheKey is intentionally created once for this tile/repaint and shared by
                // exact lookup, render-ready upload and worker scheduling.
                string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                requested.Add(cacheKey);
                Entry fallbackEntry, currentEntry;
                ResolveRenderableEntries(tile, cacheKey, styleKey, out fallbackEntry,
                    out currentEntry);
                if (currentEntry == null)
                {
                    if (!TryUploadRenderReadyField(tile, cacheKey, styleKey, system,
                        out currentEntry))
                        Schedule(tile, cacheKey, styleKey, contourInterval, virtualDetail);
                }
                if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                if (currentEntry != null) currentEntry.LastUse = ++useSequence;
                fallbackEntriesScratch[i] = fallbackEntry;
                currentEntriesScratch[i] = currentEntry;
                drawEntriesScratch[i] = currentEntry != null ? currentEntry : fallbackEntry;
            }
''',
'prepare entries in draw')

one(
'''            lastBackFoundationCoverage = MeasureFoundationGpuReadiness(visible, tiles,
                styleKey, out readyGlobal, out readyFar);''',
'''            lastBackFoundationCoverage = MeasureFoundationGpuReadiness(visible, tiles,
                currentEntriesScratch, out readyGlobal, out readyFar);''',
'readiness call')

one(
'''                rendered = RenderBackBuffer(tiles, projection,
                    mapRotation, styleKey, effectiveMode, vessel,
                    rangeMeters);''',
'''                rendered = RenderBackBuffer(tiles, drawEntriesScratch, projection,
                    mapRotation, effectiveMode, vessel, rangeMeters);''',
'normal render call')

one(
'''                bool recovered = RenderBackBuffer(tiles, projection, mapRotation, styleKey,
                    effectiveMode, vessel, rangeMeters);''',
'''                bool recovered = RenderBackBuffer(tiles, drawEntriesScratch, projection,
                    mapRotation, effectiveMode, vessel, rangeMeters);''',
'recovery render call')

one(
'''        bool RenderBackBuffer(AERISTerrainHeightTile[] tiles,
            AERISNdMapProjection projection, Matrix4x4 mapRotation, string styleKey,
            AERISTerrainDisplayMode effectiveMode, Vessel vessel, float rangeMeters)
        {''',
'''        bool RenderBackBuffer(AERISTerrainHeightTile[] tiles, Entry[] drawEntries,
            AERISNdMapProjection projection, Matrix4x4 mapRotation,
            AERISTerrainDisplayMode effectiveMode, Vessel vessel, float rangeMeters)
        {''',
'render signature')

one(
'''                float projectionThresholdMeters = Math.Max(0.25f,
                    rangeMeters / Math.Max(128f, backTarget.height) * 0.25f);
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    Entry fallbackEntry, currentEntry;
                    ResolveRenderableEntries(tile, styleKey, out fallbackEntry,
                        out currentEntry);
                    Entry drawEntry = currentEntry != null ? currentEntry : fallbackEntry;
                    if (drawEntry == null) continue;
                    EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters);''',
'''                float projectionThresholdMeters = Math.Max(0.25f,
                    rangeMeters / Math.Max(128f, backTarget.height) * 0.25f);
                double projectionCenterLatitudeDeg = UnitLatitude(
                    projection.CenterX, projection.CenterY, projection.CenterZ);
                double projectionCenterLongitudeDeg = UnitLongitude(
                    projection.CenterX, projection.CenterY);
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?
                        drawEntries[i] : null;
                    if (drawEntry == null) continue;
                    operationHealthPreparedEntryUses++;
                    EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg);''',
'render prepared entries')

one(
'''        float MeasureFoundationGpuReadiness(AERISTerrainVisibleTileSet visible,
            AERISTerrainHeightTile[] tiles, string styleKey, out int readyGlobal,
            out int readyFar)
        {''',
'''        float MeasureFoundationGpuReadiness(AERISTerrainVisibleTileSet visible,
            AERISTerrainHeightTile[] tiles, Entry[] currentEntries,
            out int readyGlobal, out int readyFar)
        {''',
'readiness signature')

one(
'''                Entry fallback, current;
                ResolveRenderableEntries(tile, styleKey, out fallback, out current);
                if (current == null || current.CoverageFraction < 0.999f) continue;''',
'''                Entry current = currentEntries != null && i < currentEntries.Length ?
                    currentEntries[i] : null;
                if (current == null || current.CoverageFraction < 0.999f) continue;
                operationHealthPreparedEntryUses++;''',
'readiness prepared entry')

one(
'''        void Schedule(AERISTerrainHeightTile tile, string styleKey,
            float contourInterval, AERISTerrainVirtualDetailProfile virtualDetail)
        {
            string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
            if (requested.Contains(cacheKey + "|PENDING")) return;
            requested.Add(cacheKey + "|PENDING");''',
'''        void Schedule(AERISTerrainHeightTile tile, string cacheKey, string styleKey,
            float contourInterval, AERISTerrainVirtualDetailProfile virtualDetail)
        {
            if (tile == null || string.IsNullOrEmpty(cacheKey) ||
                !scheduledThisFrame.Add(cacheKey)) return;''',
'schedule signature')

one(
'''        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string styleKey,
            AERISTerrainTileSystem system, out Entry entry)
        {
            entry = null;
            if (tile == null) return false;
            string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);''',
'''        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string cacheKey,
            string styleKey, AERISTerrainTileSystem system, out Entry entry)
        {
            entry = null;
            if (tile == null || string.IsNullOrEmpty(cacheKey)) return false;''',
'upload signature')

# Both insertion sites must use the index-maintaining helper.
n=s.count('                entries[cacheKey] = entry;')
if n!=2:
    raise SystemExit(f'entry insertion sites: expected 2, found {n}')
s=s.replace('                entries[cacheKey] = entry;', '                AddEntry(entry);')

one(
'''        void RemoveSupersededEntries(AERISTerrainTileKey key,
            string keepCacheKey)
        {
            supersededScratch.Clear();
            foreach (Entry entry in entries.Values)
            {
                if (entry == null || string.Equals(entry.CacheKey, keepCacheKey,
                    StringComparison.Ordinal)) continue;
                if (entry.TileKey.Equals(key)) supersededScratch.Add(entry);
            }
            for (int i = 0; i < supersededScratch.Count; i++)
                Remove(supersededScratch[i]);
            supersededScratch.Clear();
        }''',
'''        void RemoveSupersededEntries(AERISTerrainTileKey key,
            string keepCacheKey)
        {
            supersededScratch.Clear();
            List<Entry> bucket;
            if (!entriesByTile.TryGetValue(key, out bucket) || bucket == null) return;
            for (int i = 0; i < bucket.Count; i++)
            {
                Entry entry = bucket[i];
                if (entry == null || string.Equals(entry.CacheKey, keepCacheKey,
                    StringComparison.Ordinal)) continue;
                supersededScratch.Add(entry);
            }
            for (int i = 0; i < supersededScratch.Count; i++)
                Remove(supersededScratch[i]);
            supersededScratch.Clear();
        }''',
'superseded index')

one(
'''        static void EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters)
        {''',
'''        static void EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters,
            double currentCenterLatitudeDeg, double currentCenterLongitudeDeg)
        {''',
'projection signature')

one(
'''                ToLocalMeters(context.RadiusMeters,
                    entry.LastProjectionCenterLatitudeDeg,
                    entry.LastProjectionCenterLongitudeDeg,
                    UnitLatitude(context.CenterX, context.CenterY, context.CenterZ),
                    UnitLongitude(context.CenterX, context.CenterY),
                    out east, out north);''',
'''                ToLocalMeters(context.RadiusMeters,
                    entry.LastProjectionCenterLatitudeDeg,
                    entry.LastProjectionCenterLongitudeDeg,
                    currentCenterLatitudeDeg, currentCenterLongitudeDeg,
                    out east, out north);''',
'projection center reuse')

one(
'''            entry.LastProjectionCenterLatitudeDeg =
                UnitLatitude(context.CenterX, context.CenterY, context.CenterZ);
            entry.LastProjectionCenterLongitudeDeg =
                UnitLongitude(context.CenterX, context.CenterY);''',
'''            entry.LastProjectionCenterLatitudeDeg = currentCenterLatitudeDeg;
            entry.LastProjectionCenterLongitudeDeg = currentCenterLongitudeDeg;''',
'projection center commit')

one(
'''        void ResolveRenderableEntries(AERISTerrainHeightTile tile,
            string styleKey, out Entry fallback, out Entry current)
        {
            fallback = null;
            current = null;
            if (tile == null) return;
            string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
            Entry exact;
            if (entries.TryGetValue(cacheKey, out exact) && exact != null &&
                (exact.LandMesh != null || exact.WaterMesh != null)) current = exact;

            foreach (Entry candidate in entries.Values)
            {
                if (candidate == null || candidate.LandMesh == null && candidate.WaterMesh == null ||
                    ReferenceEquals(candidate, current) ||
                    !candidate.TileKey.Equals(tile.Key)) continue;''',
'''        void ResolveRenderableEntries(AERISTerrainHeightTile tile, string cacheKey,
            string styleKey, out Entry fallback, out Entry current)
        {
            fallback = null;
            current = null;
            if (tile == null || string.IsNullOrEmpty(cacheKey)) return;
            operationHealthResolveCalls++;
            Entry exact;
            if (entries.TryGetValue(cacheKey, out exact) && exact != null &&
                (exact.LandMesh != null || exact.WaterMesh != null)) current = exact;

            List<Entry> bucket;
            if (!entriesByTile.TryGetValue(tile.Key, out bucket) || bucket == null)
            {
                if (current != null && current.CoverageFraction >= 0.999f)
                    fallback = null;
                return;
            }
            operationHealthResolveCandidates += bucket.Count;
            for (int bucketIndex = 0; bucketIndex < bucket.Count; bucketIndex++)
            {
                Entry candidate = bucket[bucketIndex];
                if (candidate == null || candidate.LandMesh == null && candidate.WaterMesh == null ||
                    ReferenceEquals(candidate, current) ||
                    !candidate.TileKey.Equals(tile.Key)) continue;''',
'resolve indexed start')

# foreach replacement requires closing brace shape unchanged; for-loop uses same closing brace.

one(
'''        void EnsureResources(Rect plot, AERISTerrainDisplayMode mode,''',
'''        void AddEntry(Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.CacheKey)) return;
            entries[entry.CacheKey] = entry;
            List<Entry> bucket;
            if (!entriesByTile.TryGetValue(entry.TileKey, out bucket) || bucket == null)
            {
                bucket = new List<Entry>(4);
                entriesByTile[entry.TileKey] = bucket;
            }
            if (!bucket.Contains(entry)) bucket.Add(entry);
        }

        AERISTerrainHeightTile[] PrepareSortedTileScratch(AERISTerrainHeightTile[] source)
        {
            if (source == null || source.Length == 0) return new AERISTerrainHeightTile[0];
            if (sortedTilesScratch == null || sortedTilesScratch.Length != source.Length)
            {
                sortedTilesScratch = new AERISTerrainHeightTile[source.Length];
                operationHealthTileScratchResizes++;
            }
            Array.Copy(source, sortedTilesScratch, source.Length);
            Array.Sort(sortedTilesScratch, CompareTilesCoarseFirst);
            return sortedTilesScratch;
        }

        void EnsureEntryScratch(int count)
        {
            count = Math.Max(0, count);
            if (fallbackEntriesScratch != null && fallbackEntriesScratch.Length == count &&
                currentEntriesScratch != null && currentEntriesScratch.Length == count &&
                drawEntriesScratch != null && drawEntriesScratch.Length == count) return;
            fallbackEntriesScratch = new Entry[count];
            currentEntriesScratch = new Entry[count];
            drawEntriesScratch = new Entry[count];
        }

        void EnsureResources(Rect plot, AERISTerrainDisplayMode mode,''',
'helpers insertion')

one(
'''            entries.Remove(entry.CacheKey);
            if (entry.CoastlineResolution >=''',
'''            entries.Remove(entry.CacheKey);
            List<Entry> bucket;
            if (entriesByTile.TryGetValue(entry.TileKey, out bucket) && bucket != null)
            {
                bucket.Remove(entry);
                if (bucket.Count == 0) entriesByTile.Remove(entry.TileKey);
            }
            if (entry.CoastlineResolution >=''',
'remove index')

one(
'''            entries.Clear();
            completed.Clear();
            requested.Clear();''',
'''            entries.Clear();
            entriesByTile.Clear();
            completed.Clear();
            requested.Clear();
            scheduledThisFrame.Clear();''',
'release index')

one(
'''                "; coast_sparse_parents=" + sparseCoastalCorrectionParentCells +
                "; cpu_terrain_draw=0.");''',
'''                "; coast_sparse_parents=" + sparseCoastalCorrectionParentCells +
                "; oh_resolve_calls=" + operationHealthResolveCalls +
                "; oh_resolve_candidates=" + operationHealthResolveCandidates +
                "; oh_entry_buckets=" + entriesByTile.Count +
                "; oh_tile_scratch_resize=" + operationHealthTileScratchResizes +
                "; oh_prepared_entry_uses=" + operationHealthPreparedEntryUses +
                "; cpu_terrain_draw=0.");''',
'health telemetry')

p.write_text(s,encoding='utf-8')
print('Operation Health Pass 1 renderer patch applied')
