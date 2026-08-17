#!/usr/bin/env bash
# AERIS local command wrapper: preserve ANSI/TTY output while keeping exactly one
# current execution log under $HOME/AERIS_Logs/latest.log.
set +e

LOG_DIR="${AERIS_LOG_DIR:-$HOME/AERIS_Logs}"
LOG="$LOG_DIR/latest.log"

if [ "$#" -eq 0 ]; then
    printf '\033[1;31m[FAIL] No command supplied to AERIS log wrapper.\033[0m\n' >&2
    exit 2
fi

mkdir -p "$LOG_DIR"
MKDIR_RC=$?
if [ "$MKDIR_RC" -ne 0 ]; then
    printf '\033[1;31m[FAIL] Could not create log directory: %s\033[0m\n' "$LOG_DIR" >&2
    exit "$MKDIR_RC"
fi

# The AERIS policy is latest-one-only. Remove prior .log files in this dedicated
# directory before starting the new pseudo-TTY capture.
find "$LOG_DIR" -maxdepth 1 -type f -name '*.log' -delete

printf -v CMD '%q ' "$@"

printf '\033[1;36m===== AERIS LOG =====\033[0m\n'
printf 'Directory : %s\n' "$LOG_DIR"
printf 'Current   : %s\n' "$LOG"

script -q -e -c "$CMD" "$LOG"
RC=$?

echo
if [ "$RC" -eq 0 ]; then
    printf '\033[1;32mAERIS COMMAND EXIT CODE: %s  [PASS]\033[0m\n' "$RC"
else
    printf '\033[1;31mAERIS COMMAND EXIT CODE: %s  [FAIL]\033[0m\n' "$RC"
fi
printf '\033[1;36mLATEST LOG:\033[0m %s\n' "$LOG"

exit "$RC"
