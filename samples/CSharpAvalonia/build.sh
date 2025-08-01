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

# Determine the default RID for the platform
RID=""
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    ARCH=$(uname -m)
    if [[ "$ARCH" == "x86_64" ]]; then
        RID="osx-x64"
    elif [[ "$ARCH" == "arm64" ]]; then
        RID="osx-arm64"
    fi
elif [[ "$OSTYPE" == "linux"* ]]; then
    # Linux
    ARCH=$(uname -m)
    if [[ "$ARCH" == "x86_64" ]]; then
        RID="linux-x64"
    elif [[ "$ARCH" == "aarch64" ]]; then
        RID="linux-arm64"
    fi
else
    echo "Unsupported OS type: $OSTYPE"
    exit 1
fi

echo "Using RID: $RID"

BUILD_VERSION="$1"
RELEASE_DIR="$SCRIPT_DIR/releases"
PUBLISH_DIR="$SCRIPT_DIR/publish"

echo ""
echo "Compiling VelopackCSharpAvalonia with dotnet..."
dotnet publish -c Release --self-contained -r "$RID" -o "$PUBLISH_DIR"

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack -u VelopackCSharpAvalonia -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR"