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
[AERIS24 GPU VERTEX] Unity Editor $UNITY_VERSION was not found.
This one-time asset step must use the KSP 1.12.5-compatible Unity editor so the
custom vertex shader can be compiled into an AssetBundle.

Set the exact editor executable and rerun, for example:
  UNITY_EDITOR="\$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity" \\
    Tools/build_aeris24_gpu_shader_bundle.sh $MODE
EOF
  exit 2
fi

run_target(){
  local method="$1"
  echo "[AERIS24 GPU VERTEX] Unity=$UNITY"
  echo "[AERIS24 GPU VERTEX] method=$method"
  "$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod "$method" \
    -logFile -
}

case "$MODE" in
  windows)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildWindows
    BUNDLE="$ROOT/GameData/AERISFlightControl/Shaders/aeris_nd_gpu_vertex_projection_windows.bundle"
    ;;
  linux)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildLinux
    BUNDLE="$ROOT/GameData/AERISFlightControl/Shaders/aeris_nd_gpu_vertex_projection_linux.bundle"
    ;;
  all)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildWindows
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildLinux
    echo "[AERIS24 GPU VERTEX] bundles:"
    sha256sum \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris_nd_gpu_vertex_projection_windows.bundle" \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris_nd_gpu_vertex_projection_linux.bundle"
    exit 0
    ;;
  *)
    echo "Usage: $0 {windows|linux|all}" >&2
    exit 2
    ;;
esac

test -s "$BUNDLE" || { echo "[AERIS24 GPU VERTEX] ERROR: bundle not emitted: $BUNDLE" >&2; exit 1; }
echo "[AERIS24 GPU VERTEX] bundle ready: $BUNDLE"
sha256sum "$BUNDLE"
