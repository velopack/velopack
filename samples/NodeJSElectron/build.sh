#!/bin/bash

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
        RID="darwin-x64"
    elif [[ "$ARCH" == "arm64" ]]; then
        RID="darwin-arm64"
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
NODEJSLIB_DIR="$SCRIPT_DIR/../../src/lib-nodejs"
RELEASE_DIR="$SCRIPT_DIR/releases"
PUBLISH_DIR="$SCRIPT_DIR/out/VelopackElectronSample-$RID"

cd $NODEJSLIB_DIR

echo ""
echo "Running npm install"
call npm install

echo ""
echo "Cleaning lib-nodejs"
call npm run clean

echo ""
echo "Compiling lib-nodejs"
call npm run dev

echo ""
echo "Packing lib-nodejs"
call npm pack

cd %~dp0

echo ""
echo "Running npm install"
call npm install

echo ""
echo "Installing lib-nodejs package"
call npm install ..\..\src\lib-nodejs\velopack-0.0.0.tgz

echo ""
echo "Packing with electron-forge"
call npm run package

echo ""
echo "Creating Velopack Release"
call vpk pack -u VelopackElectronSample -v $BUILD_VERSION -o "$RELEASE_DIR" -p "$PUBLISH_DIR"