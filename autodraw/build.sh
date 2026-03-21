#!/usr/bin/env bash
set -euo pipefail

# Script lives at: autodraw/autodraw/build.sh
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR/.."
CSPROJ_DIR="$SCRIPT_DIR"
RUST_DIR="$PROJECT_ROOT/libautodraw_uinput"
APP_NAME="AutoDraw"
APP_VERSION="2.2.0"
ARCH="x86_64"
BUILD_DIR="$PROJECT_ROOT/.build_appimage"
APPIMAGE_NAME="$PROJECT_ROOT/${APP_NAME}-${ARCH}.AppImage"

# ── Clean previous build artifacts ──────────────────────────────────────────

echo "[1/7] Cleaning previous build..."
rm -rf "$BUILD_DIR" "$APPIMAGE_NAME"
mkdir -p "$BUILD_DIR"

# ── Build Rust native library ───────────────────────────────────────────────

echo "[2/7] Building Rust library (release)..."
cargo build --release --manifest-path "$RUST_DIR/Cargo.toml"

# ── Publish C# project (self-contained) ─────────────────────────────────────

echo "[3/7] Publishing C# project (self-contained linux-x64)..."
dotnet publish "$CSPROJ_DIR/Autodraw.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$BUILD_DIR/publish"

# ── Copy native library into publish output ─────────────────────────────────

echo "[4/7] Copying libautodraw_uinput.so..."
cp "$RUST_DIR/target/release/libautodraw_uinput.so" "$BUILD_DIR/publish/"

# ── Create AppDir structure ─────────────────────────────────────────────────

echo "[5/7] Creating AppDir structure..."
APPDIR="$BUILD_DIR/AppDir"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# Copy all published files into AppDir
cp -a "$BUILD_DIR/publish/." "$APPDIR/usr/bin/"

# Create .desktop file
cat > "$APPDIR/autodraw.desktop" << 'DESKTOP'
[Desktop Entry]
Name=AutoDraw
Comment=Automatic drawing tool with uinput support
Exec=autodraw
Icon=autodraw
Terminal=false
Type=Application
Categories=Graphics;
DESKTOP

# Also copy to standard location
cp "$APPDIR/autodraw.desktop" "$APPDIR/usr/share/applications/"

# Create AppRun launcher
cat > "$APPDIR/AppRun" << 'APPRUN'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "${0}")")"
exec "$HERE/usr/bin/Autodraw" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

# ── Icon ────────────────────────────────────────────────────────────────────

ICON_PNG="$CSPROJ_DIR/Assets/ico/autodraw6.png"
if [ ! -f "$ICON_PNG" ]; then
    echo "ERROR: Icon not found: $ICON_PNG"
    exit 1
fi

cp "$ICON_PNG" "$APPDIR/autodraw.png"
cp "$ICON_PNG" "$APPDIR/usr/share/icons/hicolor/256x256/apps/autodraw.png"
ln -sf "autodraw.png" "$APPDIR/.DirIcon"

# ── Package with appimagetool ───────────────────────────────────────────────

echo "[6/7] Packaging AppImage..."
appimagetool "$APPDIR" "$APPIMAGE_NAME" 2>&1

# ── Cleanup ─────────────────────────────────────────────────────────────────

echo "[7/7] Cleaning up..."
rm -rf "$BUILD_DIR"

echo ""
echo "=== Done ==="
echo "Output: $APPIMAGE_NAME"
ls -lh "$APPIMAGE_NAME"
