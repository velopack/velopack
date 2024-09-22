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
RELEASE_DIR="$SCRIPT_DIR/../releases"
PUBLISH_DIR="$SCRIPT_DIR/../publish"

cd "$SCRIPT_DIR/../../.."

echo ""
echo "Building Velopack Rust"
cargo build

echo ""
echo "Building Velopack Vpk"
dotnet build src/vpk/Velopack.Vpk/Velopack.Vpk.csproj

cd "$SCRIPT_DIR/.."

echo ""
echo "Compiling VelopackCSharpAvalonia with dotnet..."
dotnet publish -c Release --self-contained -r osx-x64 -o "$PUBLISH_DIR" -p:UseLocalVelopack=true

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
"$SCRIPT_DIR/../../../build/Debug/net8.0/vpk" pack -u VelopackCSharpAvalonia -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR"