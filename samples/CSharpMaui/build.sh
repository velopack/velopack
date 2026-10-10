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

if [[ "$OSTYPE" != "darwin"* ]]; then
    echo "MAUI does not support $OSTYPE, use build.bat on Windows."
    exit 1
fi

# Determine the default RID for the platform
ARCH=$(uname -m)
if [[ "$ARCH" == "x86_64" ]]; then
    RID="maccatalyst-x64"
else
    RID="maccatalyst-arm64"
fi

echo "Using RID: $RID"

BUILD_VERSION="$1"
RELEASE_DIR="$SCRIPT_DIR/releases"
PUBLISH_DIR="$SCRIPT_DIR/publish"

echo ""
echo "Compiling VelopackCSharpMaui with dotnet..."
dotnet build "$SCRIPT_DIR/CSharpMaui.csproj" -c Release -f net10.0-maccatalyst -r "$RID" -o "$PUBLISH_DIR" -p:ApplicationDisplayVersion=$BUILD_VERSION

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack -u VelopackCSharpMaui -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR/VelopackCSharpMaui.app"
