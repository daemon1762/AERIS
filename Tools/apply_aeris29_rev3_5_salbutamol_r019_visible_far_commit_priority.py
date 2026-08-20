#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R019 VISIBLE FAR COMMIT PRIORITY]'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'
R008 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0:
        fail('method missing: ' + signature)
    op = text.find('{', start)
    if op < 0:
        fail('method open missing: ' + signature)
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return start, i + 1
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
    fail('method close missing: ' + signature)


for path in (R, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))
renderer = R.read_text()
build = B.read_text()
prebuild = PRE.read_text()

for required in (R004, R007, R008, R018):
    if required not in renderer:
        fail('generated parent missing: ' + required)
if R019 in renderer:
    print(PREFIX + ' renderer overlay already present')
else:
    # R019 is deliberately main-thread-only. R018 runtime showed residual visible
    # deficits with upstream=0 and almost all missing FAR already RenderReady/Pending.
    # Keep the single commit lane and existing 2.00 ms hard ceiling; only prioritize
    # exact-visible RenderReady keys ahead of hidden overscan keys and raise the
    # adaptive requested budget inside the already accepted R004 rails.
    field_old = '        long operationHealthRev35R018OverscanHolAvoided;\n'
    field_new = field_old + (
        '        // ' + R019 + ': exact-visible FAR commit urgency only.\n'
        '        // No worker, rasterizer, presentation-gate, quality or range authority changes.\n'
        '        const string Rev35R019Variant = "' + R019 + '";\n'
        '        readonly HashSet<AERISTerrainTileKey> rev35R019VisibleFarKeys =\n'
        '            new HashSet<AERISTerrainTileKey>();\n'
        '        readonly Queue<string> rev35R019VisibleFoundationQueue =\n'
        '            new Queue<string>(Rev35R007FoundationQueueMaximum);\n'
        '        long operationHealthRev35R019VisiblePriorityQueued;\n'
        '        long operationHealthRev35R019VisiblePriorityBegins;\n'
        '        long operationHealthRev35R019HiddenQueueBypassed;\n'
        '        long operationHealthRev35R019Budget100;\n'
        '        long operationHealthRev35R019Budget150;\n'
        '        int operationHealthRev35R019VisibleDeficit;\n'
        '        int operationHealthRev35R019VisibleKeyCount;\n'
        '        int operationHealthRev35R019VisiblePriorityQueuePeak;\n')
    renderer, _ = replace_once(renderer, field_old, field_new,
                               'R019 identity/state fields')

    helper_anchor = '        void MeasureVisibleFoundationGpuReadiness(CelestialBody body,\n'
    helper = '''        void RefreshRev35R019VisibleFarKeys(CelestialBody body,
            AERISTerrainHeightTile[] tiles, double centerLatitudeDeg,
            double centerLongitudeDeg, float visibleRangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            rev35R019VisibleFarKeys.Clear();
            operationHealthRev35R019VisibleKeyCount = 0;
            if (body == null || tiles == null) return;

            string environmentHash = string.Empty;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null || string.IsNullOrEmpty(tile.Key.EnvironmentHash))
                    continue;
                environmentHash = tile.Key.EnvironmentHash;
                break;
            }
            if (string.IsNullOrEmpty(environmentHash)) return;

            AERISTerrainViewportFoundationPlan plan =
                AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,
                    centerLatitudeDeg, centerLongitudeDeg, visibleRangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation);
            if (plan == null || plan.FarKeys == null) return;
            for (int i = 0; i < plan.FarKeys.Length; i++)
                rev35R019VisibleFarKeys.Add(plan.FarKeys[i]);
            operationHealthRev35R019VisibleKeyCount = rev35R019VisibleFarKeys.Count;
        }

'''
    renderer, _ = replace_once(renderer, helper_anchor, helper + helper_anchor,
                               'R019 visible-key planner helper')

    capture_old = '''                tiles = PrepareSortedTileScratch(visible.Tiles);
                EnsureEntryScratch(tiles == null ? 0 : tiles.Length);

                // R008 phase 1: establish the complete current exact request set first.
'''
    capture_new = '''                tiles = PrepareSortedTileScratch(visible.Tiles);
                EnsureEntryScratch(tiles == null ? 0 : tiles.Length);
                RefreshRev35R019VisibleFarKeys(vessel.mainBody, tiles,
                    centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation);

                // R008 phase 1: establish the complete current exact request set first.
'''
    renderer, _ = replace_once(renderer, capture_old, capture_new,
                               'R019 exact visible-key refresh before admission')

    # Queue reset remains the same authority, now clearing the priority queue and
    # exact-visible key witness alongside the inherited R007 queue.
    q0, q1 = method_bounds(renderer, '        void ResetRev35R007FoundationQueue()')
    reset = renderer[q0:q1]
    reset_old = '''            rev35R007FoundationQueue.Clear();
            rev35R007FoundationQueued.Clear();'''
    reset_new = '''            rev35R007FoundationQueue.Clear();
            rev35R019VisibleFoundationQueue.Clear();
            rev35R019VisibleFarKeys.Clear();
            operationHealthRev35R019VisibleKeyCount = 0;
            rev35R007FoundationQueued.Clear();'''
    reset, _ = replace_once(reset, reset_old, reset_new,
                            'R019 queue/key reset')
    renderer = renderer[:q0] + reset + renderer[q1:]

    # Exact-visible FAR fields use a dedicated front queue. The inherited shared
    # queued HashSet still owns duplicate suppression across both queues.
    q0, q1 = method_bounds(renderer,
        '        void QueueRev35R007FoundationField(AERISTerrainHeightTile tile,')
    queue_method = renderer[q0:q1]
    queue_old = '''            if (rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum)
            {
                operationHealthRev35R007Overflow++;
                return;
            }
            rev35R007FoundationQueued.Add(cacheKey);
            rev35R007FoundationQueue.Enqueue(cacheKey);
            operationHealthRev35R007Queued++;
            operationHealthRev35R007QueuePeak = Math.Max(
                operationHealthRev35R007QueuePeak,
                rev35R007FoundationQueue.Count);'''
    queue_new = '''            int combinedQueueCount = rev35R007FoundationQueue.Count +
                rev35R019VisibleFoundationQueue.Count;
            if (combinedQueueCount >= Rev35R007FoundationQueueMaximum)
            {
                operationHealthRev35R007Overflow++;
                return;
            }
            rev35R007FoundationQueued.Add(cacheKey);
            if (rev35R019VisibleFarKeys.Contains(tile.Key))
            {
                rev35R019VisibleFoundationQueue.Enqueue(cacheKey);
                operationHealthRev35R019VisiblePriorityQueued++;
                operationHealthRev35R019VisiblePriorityQueuePeak = Math.Max(
                    operationHealthRev35R019VisiblePriorityQueuePeak,
                    rev35R019VisibleFoundationQueue.Count);
            }
            else
            {
                rev35R007FoundationQueue.Enqueue(cacheKey);
            }
            operationHealthRev35R007Queued++;
            operationHealthRev35R007QueuePeak = Math.Max(
                operationHealthRev35R007QueuePeak,
                combinedQueueCount + 1);'''
    queue_method, _ = replace_once(queue_method, queue_old, queue_new,
                                   'R019 visible/hidden queue split')
    renderer = renderer[:q0] + queue_method + renderer[q1:]

    priority_helper = '''        bool TryBeginRev35R019VisibleFoundationCommit()
        {
            while (rev35R019VisibleFoundationQueue.Count > 0)
            {
                bool bypassingHidden = rev35R007FoundationQueue.Count > 0;
                string cacheKey = rev35R019VisibleFoundationQueue.Dequeue();
                rev35R007FoundationQueued.Remove(cacheKey);
                if (!contentSnapshotValid || !requested.Contains(cacheKey))
                {
                    operationHealthRev35R007StaleSkips++;
                    continue;
                }
                if (entries.ContainsKey(cacheKey))
                {
                    operationHealthRev35R007AlreadyCommittedSkips++;
                    continue;
                }
                AERISTerrainRenderReadyHeightField field;
                if (!renderReadyFields.TryGetValue(cacheKey, out field) || field == null)
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                if (!TryBeginPendingEntryCommit(field))
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                operationHealthRev35R019VisiblePriorityBegins++;
                if (bypassingHidden)
                    operationHealthRev35R019HiddenQueueBypassed++;
                return true;
            }
            return false;
        }

'''
    begin_anchor = '        bool TryBeginRev35R007QueuedFoundationCommit()\n'
    renderer, _ = replace_once(renderer, begin_anchor,
                               priority_helper + begin_anchor,
                               'R019 priority commit helper')

    pump_old = '''                if (pendingEntryCommit == null)
                    TryBeginRev35R007QueuedFoundationCommit();'''
    pump_new = '''                if (pendingEntryCommit == null)
                {
                    if (!TryBeginRev35R019VisibleFoundationCommit())
                        TryBeginRev35R007QueuedFoundationCommit();
                }'''
    renderer, _ = replace_once(renderer, pump_old, pump_new,
                               'R019 visible priority before hidden queue')

    # Budget acceleration is strictly inside R004's accepted 0.50/1.00/1.50/2.00
    # envelope. The existing 15/20/25 ms frame guard is evaluated afterward and
    # remains authoritative. One visible deficit requests >=1.00 ms; two or more
    # request >=1.50 ms. No new hard ceiling is introduced.
    budget_anchor = '''            // Real unscaled Unity frame time is only a protective ceiling.
'''
    budget_insert = '''            int r019VisibleDeficit = Math.Max(0,
                operationHealthRev35R018VisibleRequiredFar -
                operationHealthRev35R018VisibleReadyFar);
            operationHealthRev35R019VisibleDeficit = r019VisibleDeficit;
            bool r019VisibleCommitWork = pendingEntryCommit != null ||
                rev35R019VisibleFoundationQueue.Count > 0;
            if (r019VisibleCommitWork && r019VisibleDeficit >= 2)
            {
                requestedBudget = Math.Max(requestedBudget,
                    Rev35R004BudgetOneHalfMilliseconds);
                operationHealthRev35R019Budget150++;
            }
            else if (r019VisibleCommitWork && r019VisibleDeficit == 1)
            {
                requestedBudget = Math.Max(requestedBudget,
                    Rev35R004BudgetOneMilliseconds);
                operationHealthRev35R019Budget100++;
            }

'''
    renderer, _ = replace_once(renderer, budget_anchor,
                               budget_insert + budget_anchor,
                               'R019 visible-deficit budget request')

    telemetry_anchor = (
        '                "; oh_rev35_r018_overscan_hol_avoided=" + '
        'operationHealthRev35R018OverscanHolAvoided +\n')
    telemetry_new = telemetry_anchor + (
        '                "; oh_rev35_r019_variant=" + Rev35R019Variant +\n'
        '                "; oh_rev35_r019_visible_keys=" + operationHealthRev35R019VisibleKeyCount +\n'
        '                "; oh_rev35_r019_visible_priority_queue=" + rev35R019VisibleFoundationQueue.Count +\n'
        '                "; oh_rev35_r019_visible_priority_peak=" + operationHealthRev35R019VisiblePriorityQueuePeak +\n'
        '                "; oh_rev35_r019_visible_priority_queued=" + operationHealthRev35R019VisiblePriorityQueued +\n'
        '                "; oh_rev35_r019_visible_priority_begin=" + operationHealthRev35R019VisiblePriorityBegins +\n'
        '                "; oh_rev35_r019_hidden_queue_bypass=" + operationHealthRev35R019HiddenQueueBypassed +\n'
        '                "; oh_rev35_r019_visible_deficit=" + operationHealthRev35R019VisibleDeficit +\n'
        '                "; oh_rev35_r019_budget_100=" + operationHealthRev35R019Budget100 +\n'
        '                "; oh_rev35_r019_budget_150=" + operationHealthRev35R019Budget150 +\n')
    renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                               'R019 runtime telemetry')

# Build identity and verifier wiring. This is used by FORMAL later; FAST development
# builds intentionally call xbuild directly and install only the DLL/identity.
r018_var = 'REV3_5_R018_VARIANT="' + R018 + '"\n'
r019_var = r018_var + 'REV3_5_R019_VARIANT="' + R019 + '"\n'
build, _ = replace_once(build, r018_var, r019_var,
                        'R019 build identity variable')

r018_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py"\n')
r019_verify = r018_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py"\n')
build, _ = replace_once(build, r018_verify, r019_verify,
                        'R019 build verifier')

r018_identity = (
    'printf \'rev3_5_r018_variant=%s\\n\' "$REV3_5_R018_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r019_identity = r018_identity + (
    'printf \'rev3_5_r019_variant=%s\\n\' "$REV3_5_R019_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r018_identity, r019_identity,
                        'R019 candidate identity')

selftest_line = (
    " ('OH REV3.5 R018 Visible Foundation Presentation Gate Split',"
    "'selftest_v01800_oh_rev35_r018_visible_foundation_presentation_gate_split.py'),\n")
if selftest_line not in prebuild:
    # R018 prebuild may use a slightly different label. Anchor only on the exact file.
    anchor = "'selftest_v01800_oh_rev35_r018_visible_foundation_presentation_gate_split.py'),\n"
    pos = prebuild.find(anchor)
    if pos < 0:
        fail('R018 selftest prebuild anchor missing')
    end = pos + len(anchor)
    prebuild = prebuild[:end] + (
        " ('OH REV3.5 R019 Visible FAR Commit Priority',"
        "'selftest_v01800_oh_rev35_r019_visible_far_commit_priority.py'),\n") + prebuild[end:]
else:
    prebuild = prebuild.replace(
        selftest_line,
        selftest_line +
        " ('OH REV3.5 R019 Visible FAR Commit Priority',"
        "'selftest_v01800_oh_rev35_r019_visible_far_commit_priority.py'),\n",
        1)

R.write_text(renderer)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent=' + R018)
print('priority=exact-visible FAR RenderReady before hidden overscan R007 queue')
print('budget=visible deficit 1=>>=1.00ms; deficit>=2=>>=1.50ms; R004 frame guard + 2.00ms hard ceiling retained')
print('preemption=NONE worker_change=0 rasterizer_change=0 presentation_gate_change=0 quality_change=0 range_change=0')
