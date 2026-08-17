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
CANONICAL_PROBE_GUID="d318663ef084a5e9960e7476d58db45d"
CANONICAL_SHADER_GUID="04426e32c204037dfe8a8e8172b2da8b"

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
The dynamic-terrain-colour shader requires an AERIS25-specific AssetBundle.
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

write_canonical_asset_meta(){
  cat > "$PROJECT/Assets/AERISBundleProbe.txt.meta" <<EOF
fileFormatVersion: 2
guid: $CANONICAL_PROBE_GUID
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
  cat > "$PROJECT/Assets/AERISNdExactVertexProjection.shader.meta" <<EOF
fileFormatVersion: 2
guid: $CANONICAL_SHADER_GUID
ShaderImporter:
  externalObjects: {}
  defaultTextures: []
  nonModifiableTextures: []
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
  echo "[AERIS25 GPU DYNAMIC COLOUR] canonical asset GUIDs applied"
  echo "[AERIS25 GPU DYNAMIC COLOUR] probe_guid=$CANONICAL_PROBE_GUID shader_guid=$CANONICAL_SHADER_GUID"
}

# AERIS25 runtime acceptance exposed a stale AssetBundle build-cache failure: the
# emitted Windows probe collapsed from the accepted 1203-byte container to 422 bytes
# and lost the AssetBundle container object even though the editor version string was
# still 2019.4.18f1. A deliberate shader rebuild therefore starts from a clean Unity
# import/build cache. Assets and canonical .meta files are preserved.
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

LAST_LOG_FILE=""
run_target(){
  local method="$1"
  local build_target="$2"
  local log_file="$UNITY_LOG_DIR/${build_target}.log"
  LAST_LOG_FILE="$log_file"
  mkdir -p "$UNITY_LOG_DIR"
  rm -f "$log_file"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity=$UNITY"
  echo "[AERIS25 GPU DYNAMIC COLOUR] method=$method target=$build_target"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Package Manager=DISABLED (-noUpm; GpuAssets has no package dependencies)"
  echo "[AERIS25 GPU DYNAMIC COLOUR] Unity log=$log_file"

  # Unity 2019.4's Package Manager server is an external Node process. On modern
  # Linux it can throw ERR_STREAM_DESTROYED while Winston writes to stdout after
  # Unity has already closed the shared stream. This project has no Package Manager
  # dependencies, so -noUpm removes that process from the build path and -logFile
  # writes Editor output to a real file rather than sharing the caller terminal.
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
  grep -E '\[AERIS25 GPU DYNAMIC COLOUR\]|AssetBundle|Error|Exception' "$log_file" | tail -n 100 || true
}

write_canonical_asset_meta
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

if ! grep -Fq '[AERIS25 GPU DYNAMIC COLOUR] probe semantic validation PASS' "$LAST_LOG_FILE"; then
  echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: Unity did not confirm probe semantic content" >&2
  exit 1
fi

if [[ "$MODE" == "windows" ]]; then
  ACTUAL_PROBE_SHA="$(sha256sum "$PROBE" | awk '{print $1}')"
  PROBE_SIZE="$(stat -c%s "$PROBE")"
  if [[ "$PROBE_SIZE" -lt 1000 ]]; then
    echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: Windows probe is too small; stale/collapsed AssetBundle suspected" >&2
    echo "size=$PROBE_SIZE sha256=$ACTUAL_PROBE_SHA" >&2
    exit 1
  fi
  if [[ "$ACTUAL_PROBE_SHA" == "$EXPECTED_WINDOWS_PROBE_SHA" ]]; then
    echo "[AERIS25 GPU DYNAMIC COLOUR] Windows probe historical exact-SHA gate PASS"
  else
    echo "[AERIS25 GPU DYNAMIC COLOUR] Windows probe historical SHA differs; semantic/reproducibility gate required"
    echo "historical=$EXPECTED_WINDOWS_PROBE_SHA"
    echo "generated=$ACTUAL_PROBE_SHA"
    echo "size=$PROBE_SIZE"
  fi
  # Repair/preserve the legacy probe name only after the generated probe has passed
  # Unity semantic validation, container validation, and the stale-size guard.
  cp -f "$PROBE" "$AERIS24_PROBE_WINDOWS"
  echo "[AERIS25 GPU DYNAMIC COLOUR] restored legacy probe name from validated generated bytes"
fi

echo "[AERIS25 GPU DYNAMIC COLOUR] bundle ready: $BUNDLE"
sha256sum "$BUNDLE" "$PROBE"
