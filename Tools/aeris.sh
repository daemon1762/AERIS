#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
MODE="${1:-auto}"

case "$MODE" in
  desktop)
    KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
    ;;
  laptop)
    KSP="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
    ;;
  auto)
    if [[ -d "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program" ]]; then
      KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
    elif [[ -d "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" ]]; then
      KSP="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
    else
      echo "STOP: KSP not found" >&2
      exit 2
    fi
    ;;
  *)
    echo "usage: bash Tools/aeris.sh [desktop|laptop|auto]" >&2
    exit 2
    ;;
esac

cd "$ROOT"

echo "=== AERIS ONE-COMMAND ==="

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch"
  git branch --show-current
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty"
  git status -sb
  exit 11
}

if [[ "${AERIS_AFTER_SYNC:-0}" != "1" ]]; then
  git pull --ff-only origin "$BRANCH"
  export AERIS_AFTER_SYNC=1
  exec bash "$ROOT/Tools/aeris.sh" "$MODE"
fi

STAGE="$ROOT/Tools/aeris_current_stage.sh"
if [[ ! -f "$STAGE" ]]; then
  echo "STOP: current stage runner is not published yet"
  echo "HEAD=$(git rev-parse HEAD)"
  exit 12
fi

echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

bash "$STAGE" "$KSP"

echo
echo "=== AERIS FINAL ==="
echo "HEAD=$(git rev-parse HEAD)"
git status -sb
