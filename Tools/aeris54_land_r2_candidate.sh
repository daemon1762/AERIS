#!/usr/bin/env bash
set -euo pipefail

# Default: authorized build/install + arm, then audit a new KSP session on pass 2.
# --verify-only never installs or arms. --self-test exercises only the audit code.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPECTED_BRANCH="agent/aeris54-land-r2-immutable-terrain-corridor-snapshots"
BASE="86e7fa0135dfc07016672383e0a93fc3f4dce982"
cd "$ROOT"

audit_tool() {
python3 - "$@" <<'PY'
import collections
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time

MARKER = '[AERIS54][LAND_R2]'
EVENTS = dict(QUEUE='queue_count', READ_READY='read_ready_count',
              READ_INCOMPLETE='read_incomplete_count', WORKER_COMPLETE='worker_complete_count',
              PUBLISH='publish_count', STALE_REJECT='stale_reject_count',
              DIRECTION_PENDING='pending_count', SUMMARY='summary_count', RESET='reset_count')
BOOLS = ('terrain_coverage_complete', 'obstacle_coverage_complete',
         'corridor_complete', 'missed_approach_clear')
NUMBERS = ('producer_generation', 'cycle', 'priority', 'eligible_best_rank',
           'in_flight', 'samples', 'published', 'pending')

def digest(data):
    return hashlib.sha256(data).hexdigest()

def timestamp(line):
    match = re.match(r'^\[AERIS\] \[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]', line)
    if not match:
        raise ValueError('missing logger UTC timestamp')
    return datetime.datetime.strptime(match[1], '%Y-%m-%d %H:%M:%S.%f').replace(
        tzinfo=datetime.timezone.utc).timestamp()

def log_snapshot(path):
    try:
        with open(path, 'rb') as stream:
            before = os.fstat(stream.fileno())
            data = stream.read()
            after = os.fstat(stream.fileno())
        current = os.stat(path)
        identity = (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
        if identity != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) or \
                identity != (current.st_dev, current.st_ino, current.st_size, current.st_mtime_ns):
            raise ValueError('log changed during snapshot; exit KSP and retry')
        return data, {'device': before.st_dev, 'inode': before.st_ino,
                      'offset': len(data), 'prefix_sha256': digest(data)}
    except FileNotFoundError:
        return b'', None

def segment(data, current, previous):
    # A same-inode rewrite can exceed the old offset. Hash the entire old prefix,
    # not just its length. A replaced log must prove a fresh session below.
    if previous is None:
        return data, 'NEW_LOG'
    offset = previous['offset']
    if current and current['device'] == previous['device'] and current['inode'] == previous['inode'] \
            and len(data) >= offset and digest(data[:offset]) == previous['prefix_sha256']:
        if offset and data[offset-1:offset] != b'\n':
            raise ValueError('armed log ended mid-record; re-arm after a complete log')
        return data[offset:], 'APPENDED_SEGMENT'
    return data, 'REPLACED_OR_TRUNCATED_LOG'

def audit(text, armed_at, dll_sha):
    counts = collections.Counter({key: 0 for key in list(EVENTS.values()) + [
        'max_observed_in_flight', 'corridor_complete_true_count', 'missed_clear_true_count',
        'obstacle_complete_true_count', 'pqs_forbidden_marker_count',
        'env4_db_write_suppressed_count', 'suspected_exceptions',
        'terrain_complete_publish_count', 'terrain_incomplete_count',
        'malformed_record_count', 'priority_violation_count', 'priority_proof_count']})
    errors, evidence, queues = [], [], collections.defaultdict(list)
    session, identified, identities = 0, False, 0
    for line in text.splitlines():
        if re.search(r'exception|\[ERROR\]', line, re.I):
            counts['suspected_exceptions'] += 1
        if 'ENV4_DB_WRITE_SUPPRESSED' in line:
            counts['env4_db_write_suppressed_count'] += 1
        if re.search(r'(?:PQS.*FORBIDDEN|FORBIDDEN.*PQS|LAND_R2.*(?:SYNC_PQS|PQS_SAMPLE|PQS_FALLBACK))', line, re.I):
            counts['pqs_forbidden_marker_count'] += 1
        relevant = MARKER in line or 'Dedicated logger initialized. session=' in line or \
                    '[AERIS23_RUNTIME_CANDIDATE]' in line
        if not relevant:
            continue
        try:
            if timestamp(line) <= armed_at:
                raise ValueError('record predates current arm')
            if 'Dedicated logger initialized. session=' in line:
                session += 1
                identified = False
                continue
            if '[AERIS23_RUNTIME_CANDIDATE]' in line:
                hashes = re.findall(r'\bdll_sha256=([^;\s]+)', line)
                if not session or hashes != [dll_sha]:
                    raise ValueError('runtime DLL identity missing or differs from armed DLL')
                identified = True
                identities += 1
                continue
            evidence.append(line)
            if not identified:
                raise ValueError('LAND_R2 event lacks fresh session/DLL identity')
            tail = line.split(MARKER, 1)[1].strip()
            event, fields = tail.split(' ', 1)
            if event not in EVENTS:
                raise ValueError('unknown LAND_R2 event ' + event)
            pairs = re.findall(r'(?:^|\s)([a-z_][a-z_0-9]*)=(.*?)(?=\s+[a-z_][a-z_0-9]*=|$)', fields)
            values = dict(pairs)
            if len(pairs) != len(values):
                raise ValueError('duplicate field')
            for key in ('direction', 'body', 'environment', 'producer_generation'):
                if key not in values or (key != 'body' and not values[key]):
                    raise ValueError('missing ' + key)
            for key in NUMBERS:
                if key in values:
                    if not re.fullmatch(r'[0-9]+', values[key]):
                        raise ValueError('invalid nonnegative integer ' + key)
                    values[key] = int(values[key])
            for key in BOOLS:
                if key in values:
                    if values[key].lower() not in ('true', 'false'):
                        raise ValueError('invalid boolean ' + key)
                    values[key] = values[key].lower() == 'true'
            if 'in_flight' in values:
                counts['max_observed_in_flight'] = max(counts['max_observed_in_flight'], values['in_flight'])
            for key, counter in (('corridor_complete', 'corridor_complete_true_count'),
                                 ('missed_approach_clear', 'missed_clear_true_count'),
                                 ('obstacle_coverage_complete', 'obstacle_complete_true_count')):
                counts[counter] += int(values.get(key, False))
            if event == 'QUEUE':
                for key in ('cycle', 'priority', 'eligible_best_rank', 'in_flight'):
                    if key not in values:
                        raise ValueError('QUEUE missing ' + key)
                rank, best = values['priority'], values['eligible_best_rank']
                if values['cycle'] < 1 or rank > 3 or best > 3 or values['in_flight'] < 1:
                    raise ValueError('QUEUE invalid scheduling fields')
                if rank != best:
                    counts['priority_violation_count'] += 1
                queues[(session, values['producer_generation'], values['cycle'])].append(values)
            elif event == 'SUMMARY':
                for key in ('in_flight', 'published', 'pending'):
                    if key not in values:
                        raise ValueError('SUMMARY missing ' + key)
                if values.get('authority') != 'NONE_PILOT':
                    raise ValueError('SUMMARY authority changed')
            elif event == 'WORKER_COMPLETE':
                if 'samples' not in values:
                    raise ValueError('WORKER_COMPLETE missing samples')
            elif event == 'PUBLISH':
                if any(key not in values for key in BOOLS) or values.get('authority') != 'NONE_PILOT':
                    raise ValueError('PUBLISH missing completeness/authority')
                counts['terrain_complete_publish_count'] += int(values['terrain_coverage_complete'])
                counts['terrain_incomplete_count'] += int(not values['terrain_coverage_complete'])
            elif event in ('DIRECTION_PENDING', 'READ_READY', 'READ_INCOMPLETE', 'STALE_REJECT', 'RESET'):
                if 'reason' not in values:
                    raise ValueError(event + ' missing reason')
                if event == 'READ_INCOMPLETE' or (event == 'DIRECTION_PENDING' and values['reason'] in
                        ('TERRAIN_INCOMPLETE', 'NO_REQUIRED_TERRAIN')):
                    counts['terrain_incomplete_count'] += 1
            counts[EVENTS[event]] += 1
        except (ValueError, KeyError) as exc:
            counts['malformed_record_count'] += 1
            errors.append(str(exc) + ': ' + line)
    for entries in queues.values():
        ranks = [entry['priority'] for entry in entries]
        if ranks != sorted(ranks):
            counts['priority_violation_count'] += 1
        # Both admissions must occur in the same cycle, producer generation and
        # logger session. Rank 0/1 means current ARMED/selected at that opportunity.
        if any(entry['priority'] < 2 for entry in entries) and any(entry['priority'] >= 2 for entry in entries):
            counts['priority_proof_count'] += 1
    forbidden = ('corridor_complete_true_count', 'missed_clear_true_count',
                 'obstacle_complete_true_count', 'pqs_forbidden_marker_count',
                 'env4_db_write_suppressed_count', 'suspected_exceptions',
                 'malformed_record_count', 'priority_violation_count')
    required = ('queue_count', 'read_ready_count', 'worker_complete_count', 'publish_count',
                'pending_count', 'summary_count', 'terrain_complete_publish_count',
                'terrain_incomplete_count', 'priority_proof_count')
    if any(counts[key] for key in forbidden) or counts['max_observed_in_flight'] > 2:
        verdict = 'FAIL'
    elif not identities or any(counts[key] < 1 for key in required):
        verdict = 'PENDING_RUNTIME_EVIDENCE'
    else:
        verdict = 'PASS_CANDIDATE'
    return verdict, counts, errors, evidence

def self_test():
    prefix = '[AERIS] [2026-09-18 01:00:01.000] [INFO] '
    sha = 'a' * 64
    armed = timestamp('[AERIS] [2026-09-18 01:00:00.000]')
    def event(kind, details, direction='RWY09'):
        return prefix + MARKER + ' ' + kind + ' direction=' + direction + \
            ' body=Kerbin environment=ENV4 producer_generation=0 ' + details
    good = '\n'.join([prefix + 'Dedicated logger initialized. session=new',
        prefix + '[AERIS23_RUNTIME_CANDIDATE] dll_sha256=' + sha,
        event('QUEUE', 'cycle=1 priority=1 eligible_best_rank=1 in_flight=1'),
        event('QUEUE', 'cycle=1 priority=2 eligible_best_rank=2 in_flight=2', 'RWY27'),
        event('READ_READY', 'reason='), event('WORKER_COMPLETE', 'samples=40'),
        event('PUBLISH', 'terrain_coverage_complete=True obstacle_coverage_complete=false '
              'corridor_complete=false missed_approach_clear=false authority=NONE_PILOT'),
        event('READ_INCOMPLETE', 'reason=MISSING_TILE', 'RWY27'),
        event('DIRECTION_PENDING', 'reason=TERRAIN_INCOMPLETE', 'RWY27'),
        event('SUMMARY', 'in_flight=0 published=1 pending=1 authority=NONE_PILOT', 'ALL')])
    cases = [('complete and incomplete with same-cycle priority', good, 'PASS_CANDIDATE'),
        ('no evidence', '', 'PENDING_RUNTIME_EVIDENCE'),
        ('partial publication only', good.replace('terrain_coverage_complete=True', 'terrain_coverage_complete=false'), 'PENDING_RUNTIME_EVIDENCE'),
        ('obstacle pending only', good.replace('READ_INCOMPLETE', 'READ_READY').replace('reason=TERRAIN_INCOMPLETE', 'reason=OBSTACLES_INCOMPLETE'), 'PENDING_RUNTIME_EVIDENCE'),
        ('different scheduling cycle', good.replace('cycle=1 priority=2', 'cycle=2 priority=2'), 'PENDING_RUNTIME_EVIDENCE'),
        ('wrong eligible priority', good.replace('eligible_best_rank=2', 'eligible_best_rank=0'), 'FAIL'),
        ('negative flight count', good.replace('in_flight=2', 'in_flight=-1'), 'FAIL'),
        ('flight peak outside summary', good.replace('in_flight=2', 'in_flight=3'), 'FAIL'),
        ('malformed bool', good.replace('corridor_complete=false', 'corridor_complete=maybe'), 'FAIL'),
        ('unsafe corridor flag', good.replace('corridor_complete=false', 'corridor_complete=TRUE'), 'FAIL'),
        ('unsafe missed flag', good.replace('missed_approach_clear=false', 'missed_approach_clear=True'), 'FAIL'),
        ('unsafe obstacle flag', good.replace('obstacle_coverage_complete=false', 'obstacle_coverage_complete=true'), 'FAIL'),
        ('missing publish flag', good.replace('corridor_complete=false ', ''), 'FAIL'),
        ('duplicate counter', good.replace('in_flight=2', 'in_flight=2 in_flight=0'), 'FAIL'),
        ('negative sample count', good.replace('samples=40', 'samples=-4'), 'FAIL'),
        ('unrelated DLL', good.replace(sha, 'b' * 64), 'FAIL'),
        ('old same-DLL session', good.replace('01:00:01.000', '00:59:59.000'), 'FAIL'),
        ('missing new session', good.replace('Dedicated logger initialized. session=new', 'old log'), 'FAIL'),
        ('suppressed DB writes', good + '\nENV4_DB_WRITE_SUPPRESSED', 'FAIL'),
        ('PQS forbidden', good + '\n[AERIS54][LAND_R2] PQS_FORBIDDEN', 'FAIL'),
        ('exception', good + '\nSystem.InvalidOperationException', 'FAIL')]
    lines = good.splitlines()
    lines[2], lines[3] = lines[3], lines[2]
    cases.append(('background before selected', '\n'.join(lines), 'FAIL'))
    for name, data, expected in cases:
        actual = audit(data, armed, sha)[0]
        if actual != expected:
            raise ValueError(name + ': expected ' + expected + ', got ' + actual)
        print('SELF_TEST PASS: ' + name + ' -> ' + actual)
    old = b'old session\n'
    previous = dict(device=1, inode=2, offset=len(old), prefix_sha256=digest(old))
    assert segment(old + b'new\n', previous, previous) == (b'new\n', 'APPENDED_SEGMENT')
    assert segment(b'new much longer session\n', previous, previous)[1] == 'REPLACED_OR_TRUNCATED_LOG'
    assert segment(b'new\n', previous, previous)[1] == 'REPLACED_OR_TRUNCATED_LOG'
    assert segment(old + b'new\n', dict(device=1, inode=3), previous)[1] == 'REPLACED_OR_TRUNCATED_LOG'
    assert segment(old, previous, previous)[0] == b''
    assert segment(b'new\n', previous, None)[1] == 'NEW_LOG'
    print('SELF_TEST PASS: append, same-inode longer rewrite, truncation, inode rotation, unchanged, new log')
    print('AERIS54_LAND_R2_AUDIT_SELF_TESTS=PASS')

def main():
    mode = sys.argv[1]
    if mode == 'self-test':
        self_test()
        return 0
    state_path = Path(sys.argv[2])
    if mode == 'head':
        print(json.loads(state_path.read_text())['head'] if state_path.exists() else '')
        return 0
    head, sha, log_path = sys.argv[3:6]
    if mode == 'arm':
        _, previous = log_snapshot(log_path)
        state = dict(head=head, dll_sha256=sha, armed_at=time.time(), log=previous)
        temporary = state_path.with_suffix('.tmp')
        temporary.write_text(json.dumps(state, indent=2) + '\n')
        temporary.replace(state_path)
        return 0
    state = json.loads(state_path.read_text())
    if state['head'] != head or state['dll_sha256'] != sha:
        raise ValueError('HEAD/DLL changed since arm; runtime evidence cannot be reused')
    data, current = log_snapshot(log_path)
    data, segment_kind = segment(data, current, state['log'])
    if data and not data.endswith(b'\n'):
        raise ValueError('runtime log ends mid-record; complete evidence is required')
    text = data.decode('utf-8', errors='strict')
    (state_path.parent / 'runtime-segment.log').write_bytes(data)
    verdict, counts, errors, evidence = audit(text, state['armed_at'], sha)
    print('log_segment=' + segment_kind)
    print('armed_head=' + head)
    print('installed_dll_sha256=' + sha)
    for name, value in counts.items():
        print(str(name) + '=' + str(value))
    print('priority_evidence=' + ('PROVEN' if counts['priority_proof_count'] and not counts['priority_violation_count'] else 'UNPROVEN'))
    if verdict != 'PASS_CANDIDATE':
        print('=== RUNTIME / PRIORITY EVIDENCE (last 160 events) ===')
        for line in evidence[-160:]:
            print(line)
        for error in errors[-30:]:
            print('AUDIT_ERROR: ' + error)
    print('AERIS54_LAND_R2_VERDICT=' + verdict)
    print('AERIS_CURRENT_STAGE=' + ('LAND_R2_CANDIDATE_PASS' if verdict == 'PASS_CANDIDATE' else
          'LAND_R2_CANDIDATE_FAIL' if verdict == 'FAIL' else 'WAITING_FOR_RUNTIME'))
    return 0 if verdict == 'PASS_CANDIDATE' else 50 if verdict == 'FAIL' else 51

try:
    sys.exit(main())
except (ValueError, KeyError, TypeError, OSError) as exc:
    print('STOP: audit state/evidence invalid: ' + str(exc), file=sys.stderr)
    sys.exit(52)
PY
}

if [[ $# -eq 1 && "$1" = --self-test ]]; then
  # Bash may spill this function's large Python here-document to a temp file.
  mkdir -p "$ROOT/.aeris54-land-r2.cache/self-test/tmp"
  export TMPDIR="$ROOT/.aeris54-land-r2.cache/self-test/tmp"
  audit_tool self-test
  exit 0
fi
VERIFY_ONLY=0
if [[ ${1:-} = --verify-only ]]; then VERIFY_ONLY=1; shift; fi
if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris54_land_r2_candidate.sh [--verify-only] <KSP root> | --self-test" >&2
  exit 2
fi
KSP="$1"
case "$KSP" in
  "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program") MODE=desktop ;;
  "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program") MODE=laptop ;;
  *) echo "STOP: KSP root must be the exact supported desktop or laptop path" >&2; exit 13 ;;
esac
[[ "$(git branch --show-current)" = "$EXPECTED_BRANCH" ]] || { echo 'STOP: wrong branch' >&2; exit 10; }
[[ -z "$(git status --porcelain)" ]] || { echo 'STOP: worktree dirty' >&2; git status -sb >&2; exit 11; }
git merge-base --is-ancestor "$BASE" HEAD || { echo 'STOP: accepted producer-coherence base is not an ancestor' >&2; exit 12; }
[[ -f "$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" ]] || { echo 'STOP: invalid KSP root' >&2; exit 13; }

# All runner state, compiler scratch files and cache directories are repository local.
STATE_DIR="$ROOT/.aeris54-land-r2.cache/$MODE"
mkdir -p "$STATE_DIR/tmp" "$STATE_DIR/xdg-cache" "$STATE_DIR/xdg-config" "$STATE_DIR/xdg-data"
export TMPDIR="$STATE_DIR/tmp" TMP="$STATE_DIR/tmp" TEMP="$STATE_DIR/tmp"
export XDG_CACHE_HOME="$STATE_DIR/xdg-cache" XDG_CONFIG_HOME="$STATE_DIR/xdg-config" XDG_DATA_HOME="$STATE_DIR/xdg-data"
export MONO_SHARED_DIR="$STATE_DIR/tmp" MONO_DISABLE_SHARED_AREA=1
unset MONO_PATH AERIS_LAND_R2_TEST_SUITE
STATE="$STATE_DIR/state.json"
CURRENT_HEAD="$(git rev-parse HEAD)"
BUILD_DLL="$ROOT/Source/AERISFlightControl/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
echo '=== AERIS54 / LAND-R2 CANDIDATE ==='
echo "HEAD=$CURRENT_HEAD"
echo "KSP=$KSP"
echo "verify_only=$VERIFY_ONLY"

# Preserve every start-audit architecture gate, including accepted HF2 identity,
# indexed immutable reads, planner fail-closed behavior and control-free LAND.
bash Tools/aeris54_land_r2_start_audit.sh "$KSP" > "$STATE_DIR/static-start.log"
PRODUCER=Source/AERISFlightControl/Landing/AERISApproachTerrainCorridorProducer.cs
READS=Source/AERISFlightControl/Terrain/AERISTerrainCorridorReadService.cs
require_literal() {
  grep -Fq "$1" "$2" || { echo "STOP: static gate missing: $1 in $2" >&2; exit 20; }
}
if grep -En 'FlightCtrlState|PQS\.GetSurfaceHeight|GetSurfaceHeight|PQSMod|\.pqs\b' "$PRODUCER" "$READS"; then
  echo 'STOP: forbidden control/PQS reference in LAND-R2 boundary' >&2; exit 21
fi
require_literal 'MaximumInFlightDirections = 2' "$PRODUCER"
require_literal 'AERISRuntimeLane.SafetyLand' "$PRODUCER"
require_literal 'TryLoadBatch' "$READS"
require_literal 'R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE' "$PRODUCER"
require_literal 'CorridorComplete = false' "$PRODUCER"
require_literal 'MissedApproachClear = false' "$PRODUCER"
require_literal 'ObstacleCoverageComplete = false' "$PRODUCER"
TILE=Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs
require_literal 'PQS_RUNTIME_FALLBACK_V1' "$TILE"
require_literal 'RefreshRuntimeProducerPolicyForBody' "$TILE"
require_literal 'runtimeProducerCertifiedTopologyHashes' "$TILE"
require_literal 'RuntimeProducerFallbackActiveForBody(body)' Source/AERISFlightControl/Terrain/AERISR043PreloadPtcPipeline.cs
require_literal 'RegisterRuntimeProducerFallbackForBody' Source/AERISFlightControl/Terrain/AERISR047ExactCpuProduction.cs
require_literal '!AERISTerrainTileSystem.RuntimeProducerFallbackActiveForBody(body)' Source/AERISFlightControl/Terrain/AERISR051PreloadCoastlineFastPath.cs
require_literal 'TryCreateR043PtcState(body, request, pqsHash);' Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs
require_literal '[AERIS54][ENV4_STALE_PRODUCER_TILE_DROPPED]' Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs
require_literal '[AERIS54][ENV4_STALE_PRODUCER_TILE_DROPPED]' "$TILE"
echo 'AERIS54_LAND_R2_STATIC=PASS'

if (( ! VERIFY_ONLY )) && { pgrep -x KSP.x86_64 >/dev/null || pgrep -x KSP.x64 >/dev/null; }; then
  echo 'STOP: exit KSP before installing or auditing its log' >&2
  exit 30
fi
installed_dll() {
  local targets=()
  [[ -d "$GAME_DATA" ]] || { echo 'STOP: installed AERIS directory missing' >&2; return 31; }
  mapfile -t targets < <(find "$GAME_DATA" -type f -name AERISFlightControl.dll -print)
  [[ ${#targets[@]} -eq 1 ]] || { echo 'STOP: expected exactly one installed AERIS DLL' >&2; return 31; }
  printf '%s\n' "${targets[0]}"
}

ARMED_HEAD=""
if (( ! VERIFY_ONLY )); then ARMED_HEAD="$(audit_tool head "$STATE")"; fi
if (( VERIFY_ONLY )) || [[ "$ARMED_HEAD" != "$CURRENT_HEAD" ]]; then
  BUILD_ARGS=("$MODE")
  (( ! VERIFY_ONLY )) || BUILD_ARGS+=(--no-install)
  AERIS_PRELOAD_BRANCH="$EXPECTED_BRANCH" bash Tools/AERIS_preload_build_and_go.sh "${BUILD_ARGS[@]}" 2>&1 | tee "$STATE_DIR/build.log"
  mcs -out:"$STATE_DIR/AERIS54_LAND_R2_pure_tests.exe" Tools/AERIS54_LAND_R2_pure_tests.cs
  export AERIS_KSP_MANAGED="$KSP/KSP_x64_Data/Managed"
  export AERIS_BOOTSTRAP_SOURCE="$ROOT/Source/AERISFlightControl/Core/AERISBootstrap.cs"
  mono "$STATE_DIR/AERIS54_LAND_R2_pure_tests.exe" "$BUILD_DLL" 2>&1 | tee "$STATE_DIR/pure.log"
  grep -Fxq 'AERIS54_LAND_R2_PURE_TESTS=PASS' "$STATE_DIR/pure.log"
  # The existing full Main does not include the Task 5 bootstrap integration test.
  AERIS_LAND_R2_TEST_SUITE=bootstrap-integration mono "$STATE_DIR/AERIS54_LAND_R2_pure_tests.exe" "$BUILD_DLL" 2>&1 | tee "$STATE_DIR/bootstrap.log"
  grep -Fxq 'LAND_R2_BOOTSTRAP_LIFECYCLE_INTEGRATION=PASS' "$STATE_DIR/bootstrap.log"
  [[ "$(git rev-parse HEAD)" = "$CURRENT_HEAD" && -z "$(git status --porcelain)" ]] || {
    echo 'STOP: source changed during build/tests' >&2; exit 32;
  }
  DLL_SHA="$(sha256sum "$BUILD_DLL" | awk '{print $1}')"
  echo "candidate_dll_sha256=$DLL_SHA"
  if (( VERIFY_ONLY )); then
    echo 'install=SKIPPED'
    echo 'runtime_evidence=PENDING_NOT_ARMED_BY_VERIFY_ONLY'
    echo 'AERIS54_LAND_R2_VERIFY_ONLY=PASS'
    echo 'AERIS_CURRENT_STAGE=LAND_R2_VERIFIED_RUNTIME_PENDING'
    exit 0
  fi
  TARGET="$(installed_dll)"
  cmp -s "$BUILD_DLL" "$TARGET" || { echo 'STOP: installed DLL differs from tested build' >&2; exit 33; }
  audit_tool arm "$STATE" "$CURRENT_HEAD" "$DLL_SHA" "$LOG"
  echo 'AERIS54_LAND_R2=ARMED'
  echo "installed_dll_sha256=$DLL_SHA"
  echo 'AERIS_CURRENT_STAGE=WAITING_FOR_RUNTIME'
  echo 'human_action=Launch KSP, wait for airfield/preload/LAND-R2 activity, select a runway, arm LAND when safe, disarm, exit normally, then run this same command again.'
  exit 0
fi

# Second pass never rebuilds/installs: the stored pair and actual startup DLL SHA
# must match. Keeping state permits another natural run when evidence is pending.
TARGET="$(installed_dll)"
DLL_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ -f "$BUILD_DLL" ]] && cmp -s "$BUILD_DLL" "$TARGET" || { echo 'STOP: tested build DLL changed or missing' >&2; exit 41; }
audit_tool audit "$STATE" "$CURRENT_HEAD" "$DLL_SHA" "$LOG" 2>&1 | tee "$STATE_DIR/runtime-audit.log"
