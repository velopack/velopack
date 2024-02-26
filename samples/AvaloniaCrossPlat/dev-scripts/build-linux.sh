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
ICON_PATH="$SCRIPT_DIR/../Velopack.png"

echo ""
echo "Building Velopack Rust"
cd "$SCRIPT_DIR/../../../src/Rust"
cargo build --target x86_64-unknown-linux-gnu
cp target/x86_64-unknown-linux-gnu/release/update target/release/update

echo ""
echo "Building Velopack Vpk"
cd "$SCRIPT_DIR/../../.."
dotnet build src/Velopack.Vpk/Velopack.Vpk.csproj

echo ""
cd "$SCRIPT_DIR/.."
echo "Compiling AvaloniaCrossPlat with dotnet..."
dotnet publish -c Release --self-contained -r linux-x64 -o "$PUBLISH_DIR" -p:UseLocalVelopack=true

echo ""
echo "Building Velopack Release v$BUILD_VERSION"
"$SCRIPT_DIR/../../../build/Debug/net8.0/vpk" pack -u AvaloniaCrossPlat -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR" -i "$ICON_PATH"