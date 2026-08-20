#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O17 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'

PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R018 COMPLETE FOUNDATION DEFERRED ADOPTION]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_COMPLETE_FOUNDATION_DEFERRED_ADOPTION'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def block_bounds(text, op, label):
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return op, i + 1
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    fail('block close missing: ' + label)


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0 or text.find(signature, start + 1) >= 0:
        fail('method anchor mismatch: ' + signature)
    op = text.find('{', start)
    if op < 0: fail('method opening brace missing: ' + signature)
    _, end = block_bounds(text, op, signature)
    return start, end


for path in (R, O17, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O17.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R014 not in renderer:
    fail('formal R014 generated parent required')
if R017 not in observer or '[OH_REV3_5_R017_ND_PRESENT_STALL]' not in observer:
    fail('R017 stall observer parent required')
if 'operationHealthRev35R017BlockedCoverage' not in renderer:
    fail('R017 exact blocker instrumentation must already be applied')
if R013 in renderer or 'REV3_5_R013_VARIANT=' in build or 'rev3_5_r013_variant=' in build:
    fail('rejected R013 experiment must remain absent')
if 'const float ContentPlanningHeadingStepDeg = 6f;' not in renderer:
    fail('formal cumulative 6 degree content-planning contract missing')
if R018 in renderer:
    print(PREFIX + ' renderer overlay already present')
    raise SystemExit(0)

# Identity + compact handover state. No second worker/result/presentation lane exists.
identity_old = '        const string Rev35R014Variant = "' + R014 + '";\n'
identity_new = identity_old + (
    '        const string Rev35R018Variant = "' + R018 + '";\n')
renderer, _ = replace_once(renderer, identity_old, identity_new,
                           'R018 renderer identity')

field_old = '        long operationHealthRev35R017CadenceSkips;\n'
field_new = field_old + '''        // R018: keep the last complete content material set ACTIVE while the
        // threshold-qualified next view is prepared through the existing R008/R010/R014
        // single pipeline. Only cache-key strings receive temporary lifetime protection.
        readonly HashSet<string> rev35R018ProtectedActiveKeys =
            new HashSet<string>(StringComparer.Ordinal);
        bool rev35R018DeferredAdoptionPending;
        long rev35R018TargetTerrainGeneration = -1L;
        string rev35R018TargetStyleKey = string.Empty;
        double rev35R018TargetCenterLatitudeDeg;
        double rev35R018TargetCenterLongitudeDeg;
        float rev35R018TargetRangeMeters;
        float rev35R018TargetHeadingDeg;
        bool rev35R018TargetTrackUp;
        float rev35R018TargetAnchorV;
        AERISTerrainRenderTargetOrientation rev35R018TargetOrientation;
        long operationHealthRev35R018HandoverRequested;
        long operationHealthRev35R018HandoverRetargeted;
        long operationHealthRev35R018HandoverDeferred;
        long operationHealthRev35R018HandoverReady;
        long operationHealthRev35R018HandoverAdopted;
        long operationHealthRev35R018ActiveRestore;
        long operationHealthRev35R018ActiveRestoreSafetyBlock;
        long operationHealthRev35R018ProtectedSupersededSkips;
        long operationHealthRev35R018ProtectedPruneSkips;
'''
renderer, _ = replace_once(renderer, field_old, field_new,
                           'R018 state/counters')

helper_anchor = '        bool NeedsContentRefresh(AERISTerrainTileSystem system, Vessel vessel,\n'
helpers = r'''        void Rev35R018SetDeferredTarget(AERISTerrainTileSystem system,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation, string styleKey)
        {
            rev35R018TargetTerrainGeneration =
                system == null ? -1L : system.TerrainGeneration;
            rev35R018TargetStyleKey = styleKey ?? string.Empty;
            rev35R018TargetCenterLatitudeDeg = centerLatitudeDeg;
            rev35R018TargetCenterLongitudeDeg = centerLongitudeDeg;
            rev35R018TargetRangeMeters = rangeMeters;
            rev35R018TargetHeadingDeg = mapHeadingDeg;
            rev35R018TargetTrackUp = trackUp;
            rev35R018TargetAnchorV = anchorV;
            rev35R018TargetOrientation = orientation;
        }

        bool Rev35R018NeedsDeferredTargetRefresh(AERISTerrainTileSystem system,
            Vessel vessel, double centerLatitudeDeg, double centerLongitudeDeg,
            float rangeMeters, float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation, string styleKey)
        {
            if (!rev35R018DeferredAdoptionPending || system == null ||
                vessel == null || vessel.mainBody == null) return true;
            if (rev35R018TargetTerrainGeneration != system.TerrainGeneration ||
                !string.Equals(rev35R018TargetStyleKey, styleKey,
                    StringComparison.Ordinal) ||
                rev35R018TargetTrackUp != trackUp ||
                rev35R018TargetOrientation != orientation ||
                Math.Abs(rev35R018TargetAnchorV - anchorV) > 0.001f ||
                Math.Abs(rev35R018TargetRangeMeters - rangeMeters) > 0.5f)
                return true;
            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(
                rev35R018TargetHeadingDeg, mapHeadingDeg)) >=
                ContentPlanningHeadingStepDeg) return true;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                rev35R018TargetCenterLatitudeDeg,
                rev35R018TargetCenterLongitudeDeg,
                centerLatitudeDeg, centerLongitudeDeg);
            if (double.IsNaN(displacement) || double.IsInfinity(displacement))
                return true;
            return displacement >= Math.Max(100.0,
                Math.Max(1f, rangeMeters) * 0.02);
        }

        void Rev35R018ProtectActiveSnapshotKeys()
        {
            rev35R018ProtectedActiveKeys.Clear();
            if (!contentSnapshotValid || contentVisible == null ||
                contentVisible.Tiles == null) return;
            for (int i = 0; i < contentVisible.Tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = contentVisible.Tiles[i];
                if (tile == null) continue;
                rev35R018ProtectedActiveKeys.Add(CacheKey(tile.Key,
                    tile.CreatedUtcTicks, contentStyleKey));
            }
        }

        void Rev35R018ClearDeferredAdoption()
        {
            rev35R018DeferredAdoptionPending = false;
            rev35R018ProtectedActiveKeys.Clear();
            rev35R018TargetTerrainGeneration = -1L;
            rev35R018TargetStyleKey = string.Empty;
            rev35R018TargetCenterLatitudeDeg = 0.0;
            rev35R018TargetCenterLongitudeDeg = 0.0;
            rev35R018TargetRangeMeters = 0f;
            rev35R018TargetHeadingDeg = 0f;
            rev35R018TargetTrackUp = false;
            rev35R018TargetAnchorV = 0.5f;
            rev35R018TargetOrientation =
                AERISTerrainRenderTargetOrientation.Direct;
        }

        bool Rev35R018RestoreActivePresentationScratch(
            out AERISTerrainVisibleTileSet visible,
            out AERISTerrainHeightTile[] tiles,
            out int readyGlobal, out int readyFar)
        {
            visible = contentVisible;
            tiles = sortedTilesScratch;
            readyGlobal = contentReadyGlobal;
            readyFar = contentReadyFar;
            if (!contentSnapshotValid || visible == null ||
                visible.Tiles == null || visible.Tiles.Length == 0)
                return false;

            tiles = PrepareSortedTileScratch(visible.Tiles);
            EnsureEntryScratch(tiles == null ? 0 : tiles.Length);
            if (tiles == null || tiles.Length == 0) return false;

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
                string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks,
                    contentStyleKey);
                Entry fallbackEntry, currentEntry;
                ResolveRenderableEntries(tile, cacheKey, contentStyleKey,
                    out fallbackEntry, out currentEntry);
                if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                if (currentEntry != null) currentEntry.LastUse = ++useSequence;
                fallbackEntriesScratch[i] = fallbackEntry;
                currentEntriesScratch[i] = currentEntry;
                drawEntriesScratch[i] = currentEntry != null ?
                    currentEntry : fallbackEntry;
            }

            float restoredCoverage = MeasureFoundationGpuReadiness(visible,
                tiles, currentEntriesScratch, out readyGlobal, out readyFar);
            contentFoundationCoverage = restoredCoverage;
            contentReadyGlobal = readyGlobal;
            contentReadyFar = readyFar;
            lastBackFoundationCoverage = restoredCoverage;
            lastCoverageFraction = restoredCoverage;
            operationHealthRev35R018ActiveRestore++;
            bool complete = visible.FoundationComplete &&
                restoredCoverage >= 0.999f &&
                readyFar >= visible.FarFoundationCount;
            if (!complete)
                operationHealthRev35R018ActiveRestoreSafetyBlock++;
            return complete;
        }

'''
renderer, _ = replace_once(renderer, helper_anchor, helpers + helper_anchor,
                           'R018 helper methods')

# Patch Draw only at narrow stable authorities. R008 requested-first/FAR-first and R014
# batching remain byte-for-byte inside the candidate block.
d0, d1 = method_bounds(renderer,
    '        internal AERISTerrainGpuDrawState Draw(Rect plot,')
draw = renderer[d0:d1]

for required in (
    'rasterizer.ReconcileCurrentRequests(requested);',
    'for (int admissionPass = 0; admissionPass < 2; admissionPass++)',
    'bool rev35R014ReconcileRequired = contentGeometryChanged ||',
    'rev35R014ReconciledPublicationSerial ='):
    if required not in draw:
        fail('generated R008/R014 Draw contract missing: ' + required)

geometry_old = '''            bool contentGeometryChanged = NeedsContentRefresh(system, vessel,
                centerLatitudeDeg, centerLongitudeDeg, rangeMeters, mapHeadingDeg,
                trackUp, anchorV, orientation, styleKey);
'''
geometry_new = geometry_old + '''            bool rev35R018DeferredTargetChanged =
                rev35R018DeferredAdoptionPending &&
                Rev35R018NeedsDeferredTargetRefresh(system, vessel,
                    centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation, styleKey);
            if (rev35R018DeferredAdoptionPending &&
                !rev35R018DeferredTargetChanged)
                contentGeometryChanged = false;
            if (contentGeometryChanged && contentSnapshotValid &&
                contentVisible != null)
            {
                if (!rev35R018DeferredAdoptionPending)
                {
                    rev35R018DeferredAdoptionPending = true;
                    operationHealthRev35R018HandoverRequested++;
                    Rev35R018ProtectActiveSnapshotKeys();
                }
                else
                {
                    operationHealthRev35R018HandoverRetargeted++;
                }
                Rev35R018SetDeferredTarget(system, centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation, styleKey);
            }
'''
draw, _ = replace_once(draw, geometry_old, geometry_new,
                       'R018 deferred target admission')

retry_old = '''            bool contentRetryDue = (rasterizer.PendingCount > 0 ||
                !requestedViewReady) &&
                presentationNow >= nextContentMaintenanceRealtime;
'''
retry_new = '''            bool contentRetryDue = (rasterizer.PendingCount > 0 ||
                !requestedViewReady || rev35R018DeferredAdoptionPending) &&
                presentationNow >= nextContentMaintenanceRealtime;
'''
draw, _ = replace_once(draw, retry_old, retry_new,
                       'R018 pending handover retry cadence')

capture_old = '''                visible = system.CaptureVisible(centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
                operationHealthContentCaptures++;
'''
capture_new = '''                double rev35R018CaptureLatitudeDeg =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetCenterLatitudeDeg : centerLatitudeDeg;
                double rev35R018CaptureLongitudeDeg =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetCenterLongitudeDeg : centerLongitudeDeg;
                float rev35R018CaptureRangeMeters =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetRangeMeters : rangeMeters;
                float rev35R018CaptureHeadingDeg =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetHeadingDeg : mapHeadingDeg;
                bool rev35R018CaptureTrackUp =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetTrackUp : trackUp;
                float rev35R018CaptureAnchorV =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetAnchorV : anchorV;
                AERISTerrainRenderTargetOrientation rev35R018CaptureOrientation =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetOrientation : orientation;
                string rev35R018CaptureStyleKey =
                    rev35R018DeferredAdoptionPending ?
                    rev35R018TargetStyleKey : styleKey;

                visible = system.CaptureVisible(rev35R018CaptureLatitudeDeg,
                    rev35R018CaptureLongitudeDeg, rev35R018CaptureRangeMeters,
                    rev35R018CaptureHeadingDeg, rev35R018CaptureTrackUp,
                    rev35R018CaptureAnchorV, rev35R018CaptureOrientation);
                operationHealthContentCaptures++;
                bool rev35R018CandidateAvailable =
                    visible != null && visible.Tiles != null &&
                    visible.Tiles.Length > 0;
'''
draw, _ = replace_once(draw, capture_old, capture_new,
                       'R018 stable candidate capture target')

null_old = '''                if (visible == null || visible.Tiles == null ||
                    visible.Tiles.Length == 0)
                {
                    ResetContentSnapshot();
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                    lastCoverageFraction = 0f;
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    TryPresentCoalescedFront(plot, vessel);
                    return lastDrawState;
                }

'''
null_new = '''                if (!rev35R018CandidateAvailable &&
                    !rev35R018DeferredAdoptionPending)
                {
                    ResetContentSnapshot();
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                    lastCoverageFraction = 0f;
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    TryPresentCoalescedFront(plot, vessel);
                    return lastDrawState;
                }
                if (!rev35R018CandidateAvailable &&
                    rev35R018DeferredAdoptionPending)
                {
                    operationHealthRev35R018HandoverDeferred++;
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                }

'''
draw, _ = replace_once(draw, null_old, null_new,
                       'R018 null candidate keeps ACTIVE snapshot')

# Wrap the inherited R008/R014 candidate preparation block without rewriting it.
requested_anchor = '                requested.Clear();\n'
requested_pos = draw.find(requested_anchor, draw.find('rev35R018CandidateAvailable'))
if requested_pos < 0 or draw.find(requested_anchor, requested_pos + 1) >= 0:
    fail('R018 requested.Clear candidate anchor mismatch')
draw = (draw[:requested_pos] +
        '                if (rev35R018CandidateAvailable)\n'
        '                {\n' +
        draw[requested_pos:])

assignment_old = '''                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,
                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);
                contentVisible = visible;
                contentTerrainGeneration = visible.TerrainGeneration;
                contentStyleKey = styleKey;
                contentCenterLatitudeDeg = centerLatitudeDeg;
                contentCenterLongitudeDeg = centerLongitudeDeg;
                contentRangeMeters = rangeMeters;
                contentHeadingDeg = mapHeadingDeg;
                contentTrackUp = trackUp;
                contentAnchorV = anchorV;
                contentOrientation = orientation;
                contentReadyGlobal = readyGlobal;
                contentReadyFar = readyFar;
                contentSnapshotValid = true;
                contentGpuReadyPending = true;
                nextContentMaintenanceRealtime = presentationNow +
                    ContentMaintenanceRetrySeconds;
                lastBackFoundationCoverage = contentFoundationCoverage;
                lastCoverageFraction = contentFoundationCoverage;
'''
assignment_new = '''                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,
                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);
                float rev35R018CandidateCoverage = contentFoundationCoverage;
                bool rev35R018CandidateComplete = visible.FoundationComplete &&
                    rev35R018CandidateCoverage >= 0.999f &&
                    readyFar >= visible.FarFoundationCount;
                if (!rev35R018DeferredAdoptionPending ||
                    !contentSnapshotValid || contentVisible == null ||
                    rev35R018CandidateComplete)
                {
                    if (rev35R018DeferredAdoptionPending)
                    {
                        operationHealthRev35R018HandoverReady++;
                        operationHealthRev35R018HandoverAdopted++;
                    }
                    contentVisible = visible;
                    contentTerrainGeneration = visible.TerrainGeneration;
                    contentStyleKey = rev35R018CaptureStyleKey;
                    contentCenterLatitudeDeg = rev35R018CaptureLatitudeDeg;
                    contentCenterLongitudeDeg = rev35R018CaptureLongitudeDeg;
                    contentRangeMeters = rev35R018CaptureRangeMeters;
                    contentHeadingDeg = rev35R018CaptureHeadingDeg;
                    contentTrackUp = rev35R018CaptureTrackUp;
                    contentAnchorV = rev35R018CaptureAnchorV;
                    contentOrientation = rev35R018CaptureOrientation;
                    contentReadyGlobal = readyGlobal;
                    contentReadyFar = readyFar;
                    contentSnapshotValid = true;
                    contentGpuReadyPending = true;
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                    lastBackFoundationCoverage = contentFoundationCoverage;
                    lastCoverageFraction = contentFoundationCoverage;
                    if (rev35R018DeferredAdoptionPending)
                        Rev35R018ClearDeferredAdoption();
                }
                else
                {
                    operationHealthRev35R018HandoverDeferred++;
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                }
                }

                // Candidate preparation may have overwritten the reusable scratch arrays.
                // If adoption is still pending, reconstruct only the last complete ACTIVE
                // material set and continue its exact current-position/current-heading 10 Hz
                // projection. Failure remains fail-closed and leaves the committed FRONT latch.
                if (rev35R018DeferredAdoptionPending)
                {
                    if (!Rev35R018RestoreActivePresentationScratch(
                        out visible, out tiles, out readyGlobal, out readyFar))
                    {
                        lastDrawState = AERISTerrainGpuDrawState.Partial;
                        TryPresentCoalescedFront(plot, vessel);
                        return lastDrawState;
                    }
                }
'''
draw, _ = replace_once(draw, assignment_old, assignment_new,
                       'R018 conditional atomic content publication')

# The generated block must still contain every upstream scheduling authority exactly once.
for required, expected in (
    ('rasterizer.ReconcileCurrentRequests(requested);', 1),
    ('for (int admissionPass = 0; admissionPass < 2; admissionPass++)', 1),
    ('system.CaptureVisible(', 1),
    ('contentFoundationCoverage = MeasureFoundationGpuReadiness(', 1),
    ('Rev35R018RestoreActivePresentationScratch(', 1)):
    count = draw.count(required)
    if count != expected:
        fail('R018 Draw authority count mismatch %s=%d expected=%d' %
             (required, count, expected))

renderer = renderer[:d0] + draw + renderer[d1:]

# Protect only ACTIVE material identity while the candidate is incomplete.
m0, m1 = method_bounds(renderer, '        void RemoveSupersededEntries(')
method = renderer[m0:m1]
sup_old = '''                if (entry == null || string.Equals(entry.CacheKey, keepCacheKey,
                    StringComparison.Ordinal)) continue;
                supersededScratch.Add(entry);
'''
sup_new = '''                if (entry == null || string.Equals(entry.CacheKey, keepCacheKey,
                    StringComparison.Ordinal)) continue;
                if (rev35R018DeferredAdoptionPending &&
                    rev35R018ProtectedActiveKeys.Contains(entry.CacheKey))
                {
                    operationHealthRev35R018ProtectedSupersededSkips++;
                    continue;
                }
                supersededScratch.Add(entry);
'''
method, _ = replace_once(method, sup_old, sup_new,
                         'R018 superseded ACTIVE protection')
renderer = renderer[:m0] + method + renderer[m1:]

p0, p1 = method_bounds(renderer, '        void Prune(long totalLimit)')
method = renderer[p0:p1]
prune_old = '''                foreach (Entry entry in entries.Values)
                {
                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;
                }
'''
prune_new = '''                foreach (Entry entry in entries.Values)
                {
                    if (rev35R018DeferredAdoptionPending && entry != null &&
                        rev35R018ProtectedActiveKeys.Contains(entry.CacheKey))
                    {
                        operationHealthRev35R018ProtectedPruneSkips++;
                        continue;
                    }
                    if (oldest == null || entry.LastUse < oldest.LastUse)
                        oldest = entry;
                }
'''
method, _ = replace_once(method, prune_old, prune_new,
                         'R018 LRU ACTIVE protection')
renderer = renderer[:p0] + method + renderer[p1:]

rr0, rr1 = method_bounds(renderer, '        void PruneRenderReady(long maximumBytes)')
method = renderer[rr0:rr1]
rr_old = '''                    if (requested.Contains(pair.Key) || entries.ContainsKey(pair.Key))
                        continue;
'''
rr_new = '''                    if (requested.Contains(pair.Key) ||
                        entries.ContainsKey(pair.Key) ||
                        (rev35R018DeferredAdoptionPending &&
                         rev35R018ProtectedActiveKeys.Contains(pair.Key)))
                        continue;
'''
method, _ = replace_once(method, rr_old, rr_new,
                         'R018 RenderReady ACTIVE protection')
renderer = renderer[:rr0] + method + renderer[rr1:]

# Any hard content lifecycle reset invalidates the pending handover as well.
rs0, rs1 = method_bounds(renderer, '        void ResetContentSnapshot()')
method = renderer[rs0:rs1]
reset_old = '''        {
            contentVisible = null;
'''
reset_new = '''        {
            Rev35R018ClearDeferredAdoption();
            contentVisible = null;
'''
method, _ = replace_once(method, reset_old, reset_new,
                         'R018 reset deferred state')
renderer = renderer[:rs0] + method + renderer[rs1:]

telemetry_old = (
    '                "; oh_rev35_r014_retry_reconcile=" + '
    'operationHealthRev35R014RetryReconciles +\n')
telemetry_new = telemetry_old + (
    '                "; oh_rev35_r018_variant=" + Rev35R018Variant +\n'
    '                "; oh_rev35_r018_pending=" + '
    '(rev35R018DeferredAdoptionPending ? "1" : "0") +\n'
    '                "; oh_rev35_r018_handover_requested=" + '
    'operationHealthRev35R018HandoverRequested +\n'
    '                "; oh_rev35_r018_handover_retargeted=" + '
    'operationHealthRev35R018HandoverRetargeted +\n'
    '                "; oh_rev35_r018_handover_deferred=" + '
    'operationHealthRev35R018HandoverDeferred +\n'
    '                "; oh_rev35_r018_handover_ready=" + '
    'operationHealthRev35R018HandoverReady +\n'
    '                "; oh_rev35_r018_handover_adopted=" + '
    'operationHealthRev35R018HandoverAdopted +\n'
    '                "; oh_rev35_r018_active_restore=" + '
    'operationHealthRev35R018ActiveRestore +\n'
    '                "; oh_rev35_r018_active_safety_block=" + '
    'operationHealthRev35R018ActiveRestoreSafetyBlock +\n'
    '                "; oh_rev35_r018_protected_superseded_skip=" + '
    'operationHealthRev35R018ProtectedSupersededSkips +\n'
    '                "; oh_rev35_r018_protected_prune_skip=" + '
    'operationHealthRev35R018ProtectedPruneSkips +\n')
renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                           'R018 telemetry')

# Build/prebuild wiring remains additive over R017.
r017_var = 'REV3_5_R017_VARIANT="' + R017 + '"\n'
r018_var = r017_var + 'REV3_5_R018_VARIANT="' + R018 + '"\n'
build, _ = replace_once(build, r017_var, r018_var,
                        'R018 build identity variable')

r017_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py"\n')
r018_verify = r017_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r018_complete_foundation_deferred_adoption.py"\n')
build, _ = replace_once(build, r017_verify, r018_verify,
                        'R018 build verifier')

r017_identity = (
    'printf \'rev3_5_r017_variant=%s\\n\' "$REV3_5_R017_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r018_identity = r017_identity + (
    'printf \'rev3_5_r018_variant=%s\\n\' "$REV3_5_R018_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r017_identity, r018_identity,
                        'R018 candidate identity')

r017_suite = (
    " ('OH REV3.5 R017 ND Presentation Stall Observer',"
    "'selftest_v01800_oh_rev35_r017_nd_presentation_stall_observer.py'),\n")
r018_suite = r017_suite + (
    " ('OH REV3.5 R018 Complete Foundation Deferred Adoption',"
    "'selftest_v01800_oh_rev35_r018_complete_foundation_deferred_adoption.py'),\n")
prebuild, _ = replace_once(prebuild, r017_suite, r018_suite,
                           'R018 prebuild suite')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
    'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    if forbidden in renderer:
        fail('forbidden mechanism present after R018: ' + forbidden)

for required in (
    'lastBackFoundationCoverage >= 0.999f',
    'readyFar >= visible.FarFoundationCount',
    'nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f',
    'const float ContentPlanningHeadingStepDeg = 6f;'):
    if required not in renderer:
        fail('frozen contract missing after R018: ' + required)

R.write_text(renderer)
B.write_text(build)
PRE.write_text(prebuild)

print(PREFIX + ' APPLY PASS')
print('parent_r014=' + R014)
print('parent_r017=' + R017)
print('r018=' + R018)
print('handover=ACTIVE_COMPLETE_UNTIL_CANDIDATE_COMPLETE')
print('candidate_target=LATEST_THRESHOLD_QUALIFIED_NON_SPECULATIVE')
print('candidate_complete=FoundationComplete && coverage>=0.999 && readyFar>=requiredFar')
print('r008_requested_first=RETAINED r008_far_first=RETAINED r014_batching=RETAINED')
print('active_material_protection=exact cache-key lifetime only')
print('new_worker=0 new_queue=0 new_mesh=0 new_rendertexture=0 completed_front_cache=0')
print('swap_gate_change=0 quality_change=0 cadence_change=0 exact_range_change=0')
print('R017_observer=RETAINED')
