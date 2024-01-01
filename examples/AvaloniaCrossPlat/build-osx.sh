#!/bin/bash

# Find the absolute path of the script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if version parameter is provided
if [ "$#" -ne 1 ]; then
    echo "Please provide a version number."
    echo "Usage: ./build.sh version_number"
    exit 1
fi

echo "Building Velopack"
cd "$SCRIPT_DIR/../../src/Rust"
cargo build
cd "$SCRIPT_DIR/../.."
dotnet build src/Velopack.Vpk/Velopack.Vpk.csproj
cd "$SCRIPT_DIR"

version="$1"
releasesDir="$SCRIPT_DIR/releases"

# Write to Const.cs
echo "class Const { public const string RELEASES_DIR = @\"$releasesDir\"; } " > "$(dirname "$0")/Const.cs"
echo "Const.cs file updated with releases directory ($releasesDir)."

echo "Compiling AvaloniaCrossPlatTest with dotnet..."
dotnet publish -c Release --no-self-contained -r osx-x64 -o "$(dirname "$0")/publish"

echo "class Const { public const string RELEASES_DIR = @\"{REPLACE_ME}\"; } " > "$(dirname "$0")/Const.cs"
echo "Const.cs file reset"

echo "Building Velopack Release v$version"
"$(dirname "$0")/../../build/Debug/net8.0/vpk" pack -u AvaloniaCrossPlatTest -v "$version" -o "$releasesDir" -r osx-x64 -e AvaloniaCrossPlat -p "$(dirname "$0")/publish" -c osx -i Velopack.icns