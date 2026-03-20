#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UINPUT_DEV="/dev/uinput"

# ── /dev/uinput permission check ───────────────────────────────────────────

if [ ! -e "$UINPUT_DEV" ]; then
    echo "[!] $UINPUT_DEV not found. Load the kernel module first:"
    echo "    sudo modprobe uinput"
    exit 1
fi

if [ ! -w "$UINPUT_DEV" ]; then
    echo "[!] No write permission on $UINPUT_DEV."
    read -rp "    Fix with sudo chmod 666 $UINPUT_DEV? [y/N] " answer
    if [[ "${answer,,}" == "y" ]]; then
        sudo chmod 666 "$UINPUT_DEV"
        echo "    Permission set."
    else
        echo "    Cannot continue without write access. Exiting."
        exit 1
    fi
fi

# ── Build Rust native library ──────────────────────────────────────────────

RUST_PROJECT="$SCRIPT_DIR/../libautodraw_uinput"
if [ -d "$RUST_PROJECT" ]; then
    echo "[*] Building libautodraw_uinput.so ..."
    cargo build --release --manifest-path "$RUST_PROJECT/Cargo.toml"
else
    echo "[!] Rust project not found at $RUST_PROJECT"
    exit 1
fi

# ── Run autodraw ───────────────────────────────────────────────────────────

echo "[*] Starting AutoDraw ..."
dotnet run --project "$SCRIPT_DIR/Autodraw.csproj" -- "$@"
