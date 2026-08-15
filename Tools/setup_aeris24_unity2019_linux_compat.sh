#!/usr/bin/env bash
set -euo pipefail

case "$(uname -m)" in
  x86_64|amd64) ;;
  *)
    echo "[AERIS24 UNITY COMPAT] ERROR: this helper currently supports x86_64 only." >&2
    exit 2
    ;;
esac

COMPAT_ROOT="${AERIS_UNITY2019_COMPAT_ROOT:-$HOME/Unity/compat/aeris2019}"
LIBDIR="$COMPAT_ROOT/usr/lib/x86_64-linux-gnu"
PKG="libgconf-2-4_3.2.6-7ubuntu2_amd64.deb"
URL="https://archive.ubuntu.com/ubuntu/pool/universe/g/gconf/$PKG"
EXPECTED_SHA256="ad86a4ee82b2631dab97d16736580f03108524a8040a790a6ef8b20cbd358be4"

command -v curl >/dev/null 2>&1 || {
  echo "[AERIS24 UNITY COMPAT] ERROR: curl is required (sudo apt install curl)." >&2
  exit 2
}
command -v dpkg-deb >/dev/null 2>&1 || {
  echo "[AERIS24 UNITY COMPAT] ERROR: dpkg-deb is required (package: dpkg)." >&2
  exit 2
}

if ! ldconfig -p 2>/dev/null | grep -q 'libdbus-glib-1\.so\.2'; then
  cat >&2 <<'EOF'
[AERIS24 UNITY COMPAT] ERROR: libdbus-glib-1.so.2 is missing.
Install the native Ubuntu package first:
  sudo apt update
  sudo apt install -y libdbus-glib-1-2
EOF
  exit 2
fi

mkdir -p "$COMPAT_ROOT"
TMP="$(mktemp -d)"
cleanup(){ rm -rf "$TMP"; }
trap cleanup EXIT

if [[ ! -e "$LIBDIR/libgconf-2.so.4" ]]; then
  echo "[AERIS24 UNITY COMPAT] downloading Ubuntu Jammy compatibility library"
  curl -fL "$URL" -o "$TMP/$PKG"
  ACTUAL_SHA256="$(sha256sum "$TMP/$PKG" | awk '{print $1}')"
  if [[ "$ACTUAL_SHA256" != "$EXPECTED_SHA256" ]]; then
    echo "[AERIS24 UNITY COMPAT] ERROR: SHA256 mismatch" >&2
    echo " expected=$EXPECTED_SHA256" >&2
    echo " actual=$ACTUAL_SHA256" >&2
    exit 1
  fi
  echo "[AERIS24 UNITY COMPAT] package SHA256 verified"
  dpkg-deb -x "$TMP/$PKG" "$COMPAT_ROOT"
else
  echo "[AERIS24 UNITY COMPAT] libgconf compatibility library already present"
fi

test -e "$LIBDIR/libgconf-2.so.4" || {
  echo "[AERIS24 UNITY COMPAT] ERROR: libgconf-2.so.4 was not extracted." >&2
  exit 1
}

echo "[AERIS24 UNITY COMPAT] READY"
echo "compat_root=$COMPAT_ROOT"
echo "library=$LIBDIR/libgconf-2.so.4"
echo "AERIS shader builder will auto-prepend this directory to LD_LIBRARY_PATH."
