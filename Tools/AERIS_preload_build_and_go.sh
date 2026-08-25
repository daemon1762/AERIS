#!/usr/bin/env bash
set -euo pipefail

# AERIS Preload Development - reproducible local Build & Go
#
# Default:
#   ./Tools/AERIS_preload_build_and_go.sh
#
# Explicit:
#   ./Tools/AERIS_preload_build_and_go.sh desktop
#   ./Tools/AERIS_preload_build_and_go.sh laptop
#
# Options:
#   --pull         git pull --ff-only from the tracked preload-development branch first
#   --allow-dirty  permit a dirty worktree (development PC only; provenance records diff hash)
#   --no-install   build only; do not replace the KSP GameData DLL
#
# Environment override:
#   AERIS_PRELOAD_BRANCH=<branch>

EXPECTED_BRANCH="${AERIS_PRELOAD_BRANCH:-agent/aeris36-preload-development}"

MODE="auto"
DO_PULL=0
ALLOW_DIRTY=0
DO_INSTALL=1

usage() {
    cat <<EOF
Usage:
  $0 [auto|desktop|laptop] [--pull] [--allow-dirty] [--no-install]

Examples:
  $0
  $0 desktop
  $0 laptop --pull
EOF
}

for arg in "$@"; do
    case "$arg" in
        auto|desktop|laptop)
            MODE="$arg"
            ;;
        --pull)
            DO_PULL=1
            ;;
        --allow-dirty)
            ALLOW_DIRTY=1
            ;;
        --no-install)
            DO_INSTALL=0
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "ERROR: unknown argument: $arg" >&2
            usage >&2
            exit 2
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null || true)"

if [[ -z "$ROOT" ]]; then
    echo "ERROR: script is not inside a Git worktree." >&2
    exit 1
fi

cd "$ROOT"

BRANCH="$(git --no-pager branch --show-current)"
if [[ "$BRANCH" != "$EXPECTED_BRANCH" ]]; then
    echo "ERROR: wrong branch." >&2
    echo "  expected: $EXPECTED_BRANCH" >&2
    echo "  current : $BRANCH" >&2
    exit 1
fi

if [[ "$DO_PULL" -eq 1 ]]; then
    if [[ -n "$(git status --porcelain)" ]]; then
        echo "ERROR: --pull requires a clean worktree." >&2
        git --no-pager status --short >&2
        exit 1
    fi

    echo "=== GIT SYNC ==="
    git fetch origin "$EXPECTED_BRANCH"
    git pull --ff-only origin "$EXPECTED_BRANCH"
fi

STATUS="$(git status --porcelain)"
if [[ -n "$STATUS" && "$ALLOW_DIRTY" -ne 1 ]]; then
    echo "ERROR: worktree is dirty." >&2
    echo "Commit/push the candidate first, or use --allow-dirty intentionally." >&2
    git --no-pager status --short >&2
    exit 1
fi

DESKTOP_KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
LAPTOP_KSP="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"

choose_ksp() {
    case "$MODE" in
        desktop)
            printf '%s\n' "$DESKTOP_KSP"
            ;;
        laptop)
            printf '%s\n' "$LAPTOP_KSP"
            ;;
        auto)
            local desktop_ok=0
            local laptop_ok=0

            [[ -d "$DESKTOP_KSP/KSP_x64_Data/Managed" ]] && desktop_ok=1
            [[ -d "$LAPTOP_KSP/KSP_x64_Data/Managed" ]] && laptop_ok=1

            if [[ "$desktop_ok" -eq 1 && "$laptop_ok" -eq 0 ]]; then
                printf '%s\n' "$DESKTOP_KSP"
            elif [[ "$desktop_ok" -eq 0 && "$laptop_ok" -eq 1 ]]; then
                printf '%s\n' "$LAPTOP_KSP"
            elif [[ "$desktop_ok" -eq 1 && "$laptop_ok" -eq 1 ]]; then
                echo "ERROR: both desktop and laptop KSP paths exist; specify desktop or laptop." >&2
                exit 1
            else
                echo "ERROR: no known KSP installation found." >&2
                echo "Desktop: $DESKTOP_KSP" >&2
                echo "Laptop : $LAPTOP_KSP" >&2
                exit 1
            fi
            ;;
    esac
}

KSP="$(choose_ksp)"
SRC="$ROOT/Source/AERISFlightControl"
CSPROJ="$SRC/AERISFlightControl.csproj"
BUILD_DLL="$SRC/bin/Release/AERISFlightControl.dll"

if [[ ! -f "$CSPROJ" ]]; then
    echo "ERROR: missing project: $CSPROJ" >&2
    exit 1
fi

if [[ ! -f "$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" ]]; then
    echo "ERROR: KSPDIR does not look valid: $KSP" >&2
    exit 1
fi

GIT_SHA="$(git --no-pager rev-parse HEAD)"
GIT_SHORT="$(git --no-pager rev-parse --short=12 HEAD)"

if [[ -z "$STATUS" ]]; then
    TREE_STATE="CLEAN"
    DIFF_SHA="NONE"
else
    TREE_STATE="DIRTY"
    DIFF_SHA="$(
        {
            git --no-pager diff --binary
            git --no-pager diff --cached --binary
            git ls-files --others --exclude-standard | LC_ALL=C sort
        } | sha256sum | awk '{print $1}'
    )"
fi

echo "=== AERIS PRELOAD BUILD & GO ==="
echo "branch=$BRANCH"
echo "git_sha=$GIT_SHA"
echo "tree_state=$TREE_STATE"
echo "diff_sha=$DIFF_SHA"
echo "ksp=$KSP"
echo

echo "=== CLEAN RELEASE BUILD ==="
rm -rf "$SRC/bin/Release" "$SRC/obj/Release"

(
    cd "$SRC"
    xbuild \
        /p:Configuration=Release \
        /p:KSPDIR="$KSP" \
        AERISFlightControl.csproj
)

if [[ ! -f "$BUILD_DLL" ]]; then
    echo "ERROR: build returned without DLL: $BUILD_DLL" >&2
    exit 1
fi

DLL_SHA="$(sha256sum "$BUILD_DLL" | awk '{print $1}')"

echo
echo "=== BUILD RESULT ==="
echo "dll=$BUILD_DLL"
echo "dll_sha256=$DLL_SHA"

if [[ "$DO_INSTALL" -eq 0 ]]; then
    echo "install=SKIPPED"
    echo "RESULT=PASS"
    exit 0
fi

GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"

if [[ ! -d "$GAME_DATA_ROOT" ]]; then
    echo "ERROR: missing AERIS GameData directory: $GAME_DATA_ROOT" >&2
    exit 1
fi

mapfile -t TARGETS < <(
    find "$GAME_DATA_ROOT" \
        -type f \
        -name 'AERISFlightControl.dll' \
        -print
)

if [[ "${#TARGETS[@]}" -ne 1 ]]; then
    echo "ERROR: expected exactly one installed AERISFlightControl.dll." >&2
    echo "found=${#TARGETS[@]}" >&2
    printf '%s\n' "${TARGETS[@]}" >&2
    exit 1
fi

TARGET="${TARGETS[0]}"
OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$HOME/.cache/AERIS/preload-build-backups"
BACKUP="$BACKUP_DIR/${STAMP}-${OLD_SHA}.AERISFlightControl.dll"

mkdir -p "$BACKUP_DIR"
cp -a "$TARGET" "$BACKUP"

install -m 0644 "$BUILD_DLL" "$TARGET"

if ! cmp -s "$BUILD_DLL" "$TARGET"; then
    echo "ERROR: installed DLL differs from build output." >&2
    exit 1
fi

INSTALLED_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"

PROVENANCE="$GAME_DATA_ROOT/AERIS_PRELOAD_BUILD_PROVENANCE.txt"
cat > "$PROVENANCE" <<EOF
AERIS preload-development local build
branch=$BRANCH
git_sha=$GIT_SHA
git_short=$GIT_SHORT
tree_state=$TREE_STATE
diff_sha=$DIFF_SHA
dll_sha256=$DLL_SHA
ksp_path=$KSP
installed_dll=$TARGET
previous_dll_sha256=$OLD_SHA
previous_dll_backup=$BACKUP
built_local=$(date --iso-8601=seconds)
EOF

echo
echo "=== INSTALL RESULT ==="
echo "target=$TARGET"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "installed_dll_sha256=$INSTALLED_SHA"
echo "provenance=$PROVENANCE"
echo "INSTALL_VERIFY=PASS"
echo
echo "RESULT=PASS"
