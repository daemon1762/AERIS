#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R003 REQUESTED VIEW ADMISSION]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


if not R.is_file() or not B.is_file():
    fail('generated renderer/build missing')
renderer = R.read_text()
build = B.read_text()
if R001 not in renderer or R002 not in renderer:
    fail('R002 generated parent required')
if R003 in renderer:
    print(PREFIX + ' patch already present')
    sys.exit(0)

renderer, _ = replace_once(
    renderer,
    '        const string Rev35R002Variant = "' + R002 + '";\n',
    '        const string Rev35R002Variant = "' + R002 + '";\n'
    '        const string Rev35R003Variant = "' + R003 + '";\n'
    '        const int Rev35R003MaximumStaleSkipsPerWindow = 8;\n',
    'R003 marker/cap')

renderer, _ = replace_once(
    renderer,
    '        double operationHealthRev35PackedIndexAllocMaxMs;\n',
    '        double operationHealthRev35PackedIndexAllocMaxMs;\n'
    '        long operationHealthRev35R003StalePendingCancels;\n'
    '        long operationHealthRev35R003StaleCompletedSkips;\n'
    '        long operationHealthRev35R003RelevantAdmissions;\n',
    'R003 telemetry fields')

old_window = '''            int publishedThisWindow = 0;
            mainThreadCommitStopwatch.Reset();
            mainThreadCommitStopwatch.Start();

            while (publishedThisWindow < hardMaximum)
            {
                if (pendingEntryCommit == null)
                {'''
new_window = '''            int publishedThisWindow = 0;
            int staleSkipsThisWindow = 0;
            mainThreadCommitStopwatch.Reset();
            mainThreadCommitStopwatch.Start();

            while (publishedThisWindow < hardMaximum)
            {
                // R003 anti-HOL gate: an immutable worker product may remain useful in the
                // existing render-ready RAM store, but main-thread GPU Entry construction is
                // admitted only while that exact cache key belongs to the latest requested
                // viewport. Cancel obsolete partial commits before they monopolize the one
                // staged-commit slot. No published Entry or FRONT is modified here.
                if (pendingEntryCommit != null && requested.Count > 0 &&
                    !requested.Contains(pendingEntryCommit.CacheKey))
                {
                    CancelPendingEntryCommit();
                    operationHealthRev35R003StalePendingCancels++;
                    staleSkipsThisWindow++;
                    if (staleSkipsThisWindow >= Rev35R003MaximumStaleSkipsPerWindow)
                        break;
                    continue;
                }
                if (pendingEntryCommit == null)
                {'''
renderer, _ = replace_once(renderer, old_window, new_window,
                           'R003 pending anti-HOL admission')

old_begin = '                    if (!TryBeginPendingEntryCommit(result)) continue;\n'
new_begin = '''                    if (!TryBeginPendingEntryCommit(result)) continue;
                    // TryBegin stores the immutable render-ready field first. If this result
                    // belongs to a tile/style no longer requested, retain that RAM ingredient
                    // for possible later reuse but do not spend staged GPU commit time on it.
                    if (pendingEntryCommit != null && requested.Count > 0 &&
                        !requested.Contains(pendingEntryCommit.CacheKey))
                    {
                        CancelPendingEntryCommit();
                        operationHealthRev35R003StaleCompletedSkips++;
                        staleSkipsThisWindow++;
                        if (staleSkipsThisWindow >= Rev35R003MaximumStaleSkipsPerWindow)
                            break;
                        continue;
                    }
                    operationHealthRev35R003RelevantAdmissions++;
'''
renderer, _ = replace_once(renderer, old_begin, new_begin,
                           'R003 completed-result admission')

renderer, _ = replace_once(
    renderer,
    '                "; oh_rev35_packed_index_alloc_max_ms=" + operationHealthRev35PackedIndexAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n',
    '                "; oh_rev35_packed_index_alloc_max_ms=" + operationHealthRev35PackedIndexAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r003_variant=" + Rev35R003Variant +\n'
    '                "; oh_rev35_r003_stale_pending_cancel=" + operationHealthRev35R003StalePendingCancels +\n'
    '                "; oh_rev35_r003_stale_completed_skip=" + operationHealthRev35R003StaleCompletedSkips +\n'
    '                "; oh_rev35_r003_relevant_admit=" + operationHealthRev35R003RelevantAdmissions +\n',
    'R003 telemetry append')

if 'REV3_5_R003_VARIANT="' + R003 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R002_VARIANT="' + R002 + '"\n',
        'REV3_5_R002_VARIANT="' + R002 + '"\n'
        'REV3_5_R003_VARIANT="' + R003 + '"\n',
        'build R003 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py"\n',
        'build R003 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r002_variant=%s\\n\' "$REV3_5_R002_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r002_variant=%s\\n\' "$REV3_5_R002_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r003_variant=%s\\n\' "$REV3_5_R003_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R003 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R002)
print('r003=' + R003)
print('change=REQUESTED_VIEW_MAIN_THREAD_COMMIT_ADMISSION + STALE_PENDING_CANCEL')
print('stale_skip_cap=8')
print('worker_prepare=0 speculative=0 presentation_cache=0 quality_change=0')
