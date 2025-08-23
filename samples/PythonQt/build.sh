#!/bin/bash

if [ -z "$1" ]; then
    echo "Version number is required."
    echo "Usage: ./build.sh [version] [extra_args...]"
    exit 1
fi

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
cd "$SCRIPT_DIR"

echo
echo "Installing dependencies"
uv sync --reinstall-package velopack || exit 1

echo "update_url = R\"$SCRIPT_DIR/releases\"" > _build_config.py

echo
echo "Building Python application"
uv run pyinstaller --onedir --windowed --noconfirm main.py || exit 1

echo
echo "Building Velopack Release v$1"
mkdir -p publish
cp -r dist/main/* publish/
vpk pack --packId VelopackPythonPySide6App --mainExe main -o releases --packDir publish --packVersion "$@" || exit 1

echo
echo "Cleaning up"
rm -rf build dist publish main.spec