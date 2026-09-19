#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
APPDIR="$PREFIX/share/sonoric"
BINDIR="$PREFIX/bin"
APPDIR_DESKTOP="$PREFIX/share/applications"
ICONDIR="$PREFIX/share/icons/hicolor/scalable/apps"

arch="$(uname -m)"
case "$arch" in
  x86_64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *)
    echo "Unsupported architecture: $arch" >&2
    exit 1
    ;;
esac

DIST="${DIST:-$ROOT/dist/$RID}"
if [ ! -x "$DIST/Sonoric" ]; then
  DIST="$("$ROOT/publish-linux.sh")"
fi

mkdir -p "$APPDIR" "$BINDIR" "$APPDIR_DESKTOP" "$ICONDIR"
rm -rf "$APPDIR"
mkdir -p "$APPDIR"
cp -a "$DIST"/. "$APPDIR/"
install -m 755 "$APPDIR/Sonoric" "$APPDIR/Sonoric"
ln -sfn "$APPDIR/Sonoric" "$BINDIR/sonoric"
install -m 644 "$ROOT/packaging/linux/sonoric.svg" "$ICONDIR/sonoric.svg"
sed -e "s|@EXEC@|$APPDIR/Sonoric|g" -e "s|@ICON@|sonoric|g" \
  "$ROOT/packaging/linux/sonoric.desktop.in" > "$APPDIR_DESKTOP/sonoric.desktop"
chmod 644 "$APPDIR_DESKTOP/sonoric.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$APPDIR_DESKTOP" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q "$PREFIX/share/icons/hicolor" >/dev/null 2>&1 || true
fi

if ! ldconfig -p 2>/dev/null | grep -q 'libvlc\.so'; then
  echo "libvlc not found. Install VLC from the distro package manager:"
  echo "  Debian/Ubuntu:  sudo apt install vlc"
  echo "  Fedora:         sudo dnf install vlc"
  echo "  Arch:           sudo pacman -S vlc"
  echo "  openSUSE:       sudo zypper install vlc"
fi

echo "Installed: $BINDIR/sonoric"
echo "Launch with: sonoric"
