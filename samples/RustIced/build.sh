#!/bin/bash
set -e

# Find the absolute path of the script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if version parameter is provided
if [ "$#" -ne 1 ]; then
    echo "Version number is required."
    echo "Usage: ./build.sh [version]"
    exit 1
fi

BUILD_VERSION="$1"
RELEASE_DIR="$SCRIPT_DIR/releases"
PUBLISH_DIR="$SCRIPT_DIR/publish"

echo ""
echo "Compiling Rust/Iced with cargo..."
cargo build -r
mkdir publish
cp target/release/velorusticedsample publish/VeloRustIcedSample

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack -u VeloRustIcedSample -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR"