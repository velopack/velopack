#!/bin/bash

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
PUBLISH_DIR="$SCRIPT_DIR/build-rel/Release"

echo ""
echo "Compiling velopack_libc..."
cargo build -p velopack_libc

echo ""
echo "Compiling VelopackCppWidgets with cmake..."
cmake -S. -Bbuild-rel -DCMAKE_BUILD_TYPE=Release
cmake --build build-rel -j

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack -u VelopackCppWidgets -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR" -e main