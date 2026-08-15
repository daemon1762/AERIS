#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/GpuAssets"
MODE="${1:-windows}"
UNITY_VERSION="2019.4.18f1"

find_unity(){
  if [[ -n "${UNITY_EDITOR:-}" && -x "${UNITY_EDITOR}" ]]; then
    printf '%s\n' "$UNITY_EDITOR"
    return 0
  fi
  local candidates=(
    "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
    "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
    "/opt/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"
    "/opt/unity/Editor/Unity"
  )
  local item
  for item in "${candidates[@]}"; do
    if [[ -x "$item" ]]; then printf '%s\n' "$item"; return 0; fi
  done
  return 1
}

UNITY="$(find_unity || true)"
if [[ -z "$UNITY" ]]; then
  cat >&2 <<EOF
[AERIS25 GPU DYNAMIC COLOUR] Unity Editor $UNITY_VERSION was not found.
The new dynamic-terrain-colour shader requires an AERIS25-specific AssetBundle.
Set UNITY_EDITOR to the KSP 1.12.5-compatible Unity editor and rerun.
EOF
  exit 2
fi

UNITY_COMPAT_ROOT="${AERIS_UNITY2019_COMPAT_ROOT:-$HOME/Unity/compat/aeris2019}"
UNITY_COMPAT_LIB="$UNITY_COMPAT_ROOT/usr/lib/x86_64-linux-gnu"
if [[ -e "$UNITY_COMPAT_LIB/libgconf-2.so.4" ]]; then
  export LD_LIBRARY_PATH="$UNITY_COMPAT_LIB${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity 2019 compat=$UNITY_COMPAT_LIB"
fi
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export TERM=xterm

run_target(){
  local method="$1"
  local build_target="$2"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity=$UNITY"
  echo "[AERIS25 GPU DYNAMIC COLOUR] method=$method target=$build_target"
  "$UNITY" -batchmode -nographics -quit \
    -buildTarget "$build_target" \
    -projectPath "$PROJECT" \
    -executeMethod "$method" \
    -logFile -
}

case "$MODE" in
  windows)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildWindows Win64
    BUNDLE="$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
    ;;
  linux)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildLinux Linux64
    BUNDLE="$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
    ;;
  all)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildWindows Win64
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildLinux Linux64
    sha256sum \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle" \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
    exit 0
    ;;
  *)
    echo "Usage: $0 {windows|linux|all}" >&2
    exit 2
    ;;
esac

test -s "$BUNDLE" || { echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: bundle not emitted: $BUNDLE" >&2; exit 1; }
echo "[AERIS25 GPU DYNAMIC COLOUR] bundle ready: $BUNDLE"
sha256sum "$BUNDLE"
