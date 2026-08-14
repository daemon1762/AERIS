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

# Unity 2019.4 still links against legacy GConf on Linux. Modern Ubuntu releases
# no longer package libgconf-2-4, so AERIS may keep the old runtime library in an
# isolated user-owned compatibility directory instead of mixing old distro repos.
UNITY_COMPAT_ROOT="${AERIS_UNITY2019_COMPAT_ROOT:-$HOME/Unity/compat/aeris2019}"
UNITY_COMPAT_LIB="$UNITY_COMPAT_ROOT/usr/lib/x86_64-linux-gnu"
if [[ -e "$UNITY_COMPAT_LIB/libgconf-2.so.4" ]]; then
  export LD_LIBRARY_PATH="$UNITY_COMPAT_LIB${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  echo "[AERIS24 GPU VERTEX] Unity 2019 compat=$UNITY_COMPAT_LIB"
fi

# The Unity 2019.4 LicensingClient bundles an older .NET runtime which can fail on
# modern Ubuntu ICU versions before it can connect to the Hub licensing service.
# Restrict the documented .NET invariant-globalization switch to this Unity build
# process and its children only. It does not alter the shell, KSP, or system locale.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
echo "[AERIS24 GPU VERTEX] LicensingClient globalization=invariant (process-local)"

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
