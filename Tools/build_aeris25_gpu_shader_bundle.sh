#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/GpuAssets"
MODE="${1:-windows}"
UNITY_VERSION="2019.4.18f1"
EXPECTED_WINDOWS_PROBE_SHA="6465e6dfa7c9809a734d5ce85b202b49ea6ee5fcaac19d55d4b75bd532a35f0d"
AERIS24_PROBE_WINDOWS="$ROOT/GameData/AERISFlightControl/Shaders/aeris_gpu_bundle_probe_windows.bundle"
AERIS25_PROBE_WINDOWS="$ROOT/GameData/AERISFlightControl/Shaders/aeris25_gpu_dynamic_colour_probe_windows.bundle"
UNITY_LOG_DIR="$ROOT/.aeris25-unity-logs"

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

# AERIS25 runtime acceptance exposed a stale AssetBundle build-cache failure: the
# emitted Windows probe collapsed from the accepted 1203-byte container to 422 bytes
# and lost the AssetBundle container object even though the editor version string was
# still 2019.4.18f1. A deliberate shader rebuild must therefore start from a clean
# Unity import/build cache. Assets and ProjectSettings are preserved.
clean_unity_cache(){
  echo "[AERIS25 GPU DYNAMIC COLOUR] clean Unity project cache: Library/Temp/obj"
  rm -rf "$PROJECT/Library" "$PROJECT/Temp" "$PROJECT/obj"
}

bundle_has_container(){
  python3 - "$1" <<'PY'
from pathlib import Path
import sys
p=Path(sys.argv[1])
b=p.read_bytes()
if not b.startswith(b"UnityFS\x00"):
    raise SystemExit(1)
if b"AssetBundle" not in b:
    raise SystemExit(2)
PY
}

run_target(){
  local method="$1"
  local build_target="$2"
  local log_file="$UNITY_LOG_DIR/${build_target}.log"
  mkdir -p "$UNITY_LOG_DIR"
  rm -f "$log_file"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity=$UNITY"
  echo "[AERIS25 GPU DYNAMIC COLOUR] method=$method target=$build_target"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Package Manager=DISABLED (-noUpm; GpuAssets has no package dependencies)"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity log=$log_file"

  # Unity 2019.4's Package Manager server is an external Node process. On modern
  # Linux it can throw ERR_STREAM_DESTROYED while Winston writes to a stdout stream
  # that Unity has already closed. This project has an empty Packages dependency set,
  # so keep UPM entirely out of the AssetBundle generation path. Also write the Unity
  # Editor log to a real file rather than '-logFile -' so no child logger shares the
  # caller's terminal stream during Editor shutdown.
  set +e
  "$UNITY" -batchmode -nographics -quit -noUpm \
    -buildTarget "$build_target" \
    -projectPath "$PROJECT" \
    -executeMethod "$method" \
    -logFile "$log_file"
  local status=$?
  set -e

  if [[ $status -ne 0 ]]; then
    echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: Unity exited status=$status" >&2
    echo "[AERIS25 GPU DYNAMIC COLOUR] ---- Unity log tail ----" >&2
    tail -n 160 "$log_file" >&2 || true
    echo "[AERIS25 GPU DYNAMIC COLOUR] ---- end Unity log ----" >&2
    return "$status"
  fi

  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity batch process exited cleanly"
  grep -E '\[AERIS25 GPU DYNAMIC COLOUR\]|AssetBundle|Error|Exception' "$log_file" | tail -n 80 || true
}

clean_unity_cache

case "$MODE" in
  windows)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildWindows Win64
    BUNDLE="$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
    PROBE="$AERIS25_PROBE_WINDOWS"
    ;;
  linux)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildLinux Linux64
    BUNDLE="$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
    PROBE="$ROOT/GameData/AERISFlightControl/Shaders/aeris25_gpu_dynamic_colour_probe_linux.bundle"
    ;;
  all)
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildWindows Win64
    run_target AERIS.Editor.BuildAERISGpuAssets.BuildLinux Linux64
    sha256sum \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle" \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris25_gpu_dynamic_colour_probe_windows.bundle" \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle" \
      "$ROOT/GameData/AERISFlightControl/Shaders/aeris25_gpu_dynamic_colour_probe_linux.bundle"
    exit 0
    ;;
  *)
    echo "Usage: $0 {windows|linux|all}" >&2
    exit 2
    ;;
esac

test -s "$BUNDLE" || { echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: bundle not emitted: $BUNDLE" >&2; exit 1; }
test -s "$PROBE" || { echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: probe not emitted: $PROBE" >&2; exit 1; }

if ! bundle_has_container "$BUNDLE"; then
  echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: shader bundle lacks Unity AssetBundle container metadata: $BUNDLE" >&2
  exit 1
fi
if ! bundle_has_container "$PROBE"; then
  echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: probe lacks Unity AssetBundle container metadata: $PROBE" >&2
  exit 1
fi

if [[ "$MODE" == "windows" ]]; then
  ACTUAL_PROBE_SHA="$(sha256sum "$PROBE" | awk '{print $1}')"
  if [[ "$ACTUAL_PROBE_SHA" != "$EXPECTED_WINDOWS_PROBE_SHA" ]]; then
    echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: Windows compatibility control probe SHA mismatch" >&2
    echo "expected=$EXPECTED_WINDOWS_PROBE_SHA" >&2
    echo "actual=$ACTUAL_PROBE_SHA" >&2
    echo "size=$(stat -c%s "$PROBE")" >&2
    echo "Refusing KSP install; AssetBundle generation environment is not accepted." >&2
    exit 1
  fi
  # The AERIS25 probe is byte-identical to the accepted AERIS24 compatibility probe.
  # Repair/preserve the frozen AERIS24 probe name only after the exact SHA gate passes.
  cp -f "$PROBE" "$AERIS24_PROBE_WINDOWS"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Windows probe compatibility gate PASS"
  echo "[AERIS25 GPU DYNAMIC COLOUR] restored frozen AERIS24 probe from exact accepted bytes"
fi

echo "[AERIS25 GPU DYNAMIC COLOUR] bundle ready: $BUNDLE"
sha256sum "$BUNDLE" "$PROBE"
