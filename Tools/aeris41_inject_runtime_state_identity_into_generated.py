#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_runtime_state_identity_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V5_CURVE2_EXACT_REPAIR"
if candidate not in obs:
    raise SystemExit("AERIS41 state-identity observer candidate marker missing")
if candidate not in run:
    raise SystemExit("AERIS41 state-identity runner candidate marker missing")

# The generated R041 stages share one on-disk state directory. HEAD + DLL SHA is
# insufficient identity for generated candidates: a different generated recipe
# can exist at the same canonical HEAD. Bind state to the concrete candidate and
# fail closed if the installed managed DLL does not actually contain that marker.
state_locals_old = '''  local state_head state_sha state_offset state_log
  state_head="$(state_value head || true)"
  state_sha="$(state_value installed_dll_sha || true)"
  state_offset="$(state_value log_offset || true)"
  state_log="$(state_value log || true)"

  [[ "$state_head" = "$HEAD" ]] || return 1
  [[ -n "$state_sha" && -n "$state_offset" && "$state_log" = "$LOG" ]] || return 1'''
state_locals_new = '''  local state_schema state_candidate state_head state_sha state_offset state_log
  state_schema="$(state_value state_schema || true)"
  state_candidate="$(state_value candidate || true)"
  state_head="$(state_value head || true)"
  state_sha="$(state_value installed_dll_sha || true)"
  state_offset="$(state_value log_offset || true)"
  state_log="$(state_value log || true)"

  [[ "$state_schema" = "R041_CANDIDATE_IDENTITY_V1" ]] || return 1
  [[ "$state_candidate" = "$CANDIDATE" ]] || return 1
  [[ "$state_head" = "$HEAD" ]] || return 1
  [[ -n "$state_sha" && -n "$state_offset" && "$state_log" = "$LOG" ]] || return 1'''
if run.count(state_locals_old) != 1:
    raise SystemExit("AERIS41 state-identity harvest marker not unique")
run = run.replace(state_locals_old, state_locals_new, 1)

helper_marker = '''write_artifacts() {'''
helper = r'''candidate_marker_in_dll() {
  local dll="$1"
  python3 - "$dll" "$CANDIDATE" <<'PY'
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
marker = sys.argv[2]
data = path.read_bytes()
# CLR user-string metadata is UTF-16LE; keep an ASCII fallback for tooling
# variation and fail closed if neither representation exists.
found = marker.encode("utf-16le") in data or marker.encode("ascii") in data
raise SystemExit(0 if found else 1)
PY
}

'''
if run.count(helper_marker) != 1:
    raise SystemExit("AERIS41 state-identity helper marker not unique")
run = run.replace(helper_marker, helper + helper_marker, 1)

sha_check_old = '''  local installed_sha
  installed_sha="$(sha256sum "${targets[0]}" | awk '{print $1}')"
  [[ "$installed_sha" = "$state_sha" ]] || return 1

  [[ -f "$LOG" ]] || {'''
sha_check_new = '''  local installed_sha
  installed_sha="$(sha256sum "${targets[0]}" | awk '{print $1}')"
  [[ "$installed_sha" = "$state_sha" ]] || return 1
  if ! candidate_marker_in_dll "${targets[0]}"; then
    echo "INFO: installed DLL lacks current R041 candidate marker; state will be rebuilt" >&2
    return 1
  fi

  [[ -f "$LOG" ]] || {'''
if run.count(sha_check_old) != 1:
    raise SystemExit("AERIS41 state-identity installed-SHA marker not unique")
run = run.replace(sha_check_old, sha_check_new, 1)

waiting_old = '''    echo "observer_begin_seen=$begin_seen"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
    return 0'''
waiting_new = '''    local log_growth
    log_growth=$((current_size - state_offset))
    (( log_growth < 0 )) && log_growth=$current_size
    echo "state_schema=$state_schema"
    echo "state_candidate=$state_candidate"
    echo "installed_candidate_marker=true"
    echo "log_offset=$state_offset"
    echo "log_size=$current_size"
    echo "log_growth_bytes=$log_growth"
    echo "observer_begin_seen=$begin_seen"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
    return 0'''
if run.count(waiting_old) != 1:
    raise SystemExit("AERIS41 state-identity waiting marker not unique")
run = run.replace(waiting_old, waiting_new, 1)

installed_sha_old = '''INSTALLED_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"

LOG_OFFSET=0'''
installed_sha_new = '''INSTALLED_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
if ! candidate_marker_in_dll "$TARGET"; then
  echo "STOP: installed DLL lacks current R041 candidate marker" >&2
  exit 24
fi

LOG_OFFSET=0'''
if run.count(installed_sha_old) != 1:
    raise SystemExit("AERIS41 state-identity post-install marker not unique")
run = run.replace(installed_sha_old, installed_sha_new, 1)

state_write_old = '''cat > "$STATE" <<EOF
head=$HEAD
installed_dll_sha=$INSTALLED_SHA'''
state_write_new = '''cat > "$STATE" <<EOF
state_schema=R041_CANDIDATE_IDENTITY_V1
candidate=$CANDIDATE
head=$HEAD
installed_dll_sha=$INSTALLED_SHA'''
if run.count(state_write_old) != 1:
    raise SystemExit("AERIS41 state-identity state-write marker not unique")
run = run.replace(state_write_old, state_write_new, 1)

install_echo_old = '''echo "=== R041 ALL-BODY HEIGHT CHAIN INSTALLED ==="
echo "dll_sha256=$INSTALLED_SHA"'''
install_echo_new = '''echo "=== R041 ALL-BODY HEIGHT CHAIN INSTALLED ==="
echo "state_schema=R041_CANDIDATE_IDENTITY_V1"
echo "candidate=$CANDIDATE"
echo "installed_candidate_marker=true"
echo "dll_sha256=$INSTALLED_SHA"'''
if run.count(install_echo_old) != 1:
    raise SystemExit("AERIS41 state-identity install-report marker not unique")
run = run.replace(install_echo_old, install_echo_new, 1)

for token in (
    "R041_CANDIDATE_IDENTITY_V1",
    "candidate_marker_in_dll",
    "state_candidate=$state_candidate",
    "installed_candidate_marker=true",
    "log_growth_bytes=$log_growth",
):
    if token not in run:
        raise SystemExit("AERIS41 state-identity generated runner lost token: " + token)

runner_path.write_text(run, encoding="utf-8")
