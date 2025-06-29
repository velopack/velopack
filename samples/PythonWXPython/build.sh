#!/bin/bash
set -e

# Find the absolute path of the script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if version parameter is provided
if [ "$#" -lt 1 ]; then
    echo "Version number is required."
    echo "Usage: ./build.sh [version] [extra_args...]"
    exit 1
fi

cd "$SCRIPT_DIR"

# Installing dependencies
echo ""
echo "Installing dependencies..."
uv sync

# Generate build config
# Note: In Python, we use r'' for raw strings. The path needs to be absolute.
echo "update_url = r'$SCRIPT_DIR/releases'" > _build_config.py

# Building Python application
echo ""
echo "Building Python application..."
uv run pyinstaller --onedir --windowed --noconfirm main.py

# Building Velopack Release
echo ""
echo "Building Velopack Release v$1..."
mkdir -p publish
cp -r dist/main/* publish/
vpk pack --packId VelopackPythonSampleApp --mainExe main -o releases --packDir publish --packVersion "$@"

# Cleaning up
echo ""
echo "Cleaning up..."
rm -rf build
rm -rf dist
rm -rf publish
rm -f main.spec

echo "Done."
