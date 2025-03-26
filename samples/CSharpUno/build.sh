#!/bin/bash

# Check if version parameter is provided
if [ "$#" -ne 1 ]; then
    echo "Version number is required."
    echo "Usage: ./build.sh [version]"
    exit 1
fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

BUILD_VERSION="$1"
RELEASE_DIR="$SCRIPT_DIR/UnoSample/releases"
PUBLISH_DIR="$SCRIPT_DIR/UnoSample/publish"

echo ""
echo Compiling CSharpUno with dotnet...
dotnet publish -c Release --framework net9.0-desktop -o "$PUBLISH_DIR" UnoSample/UnoSample.csproj

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
vpk pack -u CSharpUno -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR" --mainExe UnoSample.exe