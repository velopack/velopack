@echo off
setlocal enabledelayedexpansion

:: Check if version parameter is provided
if "%~1"=="" (
    echo Please provide a version number.
    echo Usage: build.bat version_number
    exit /b 1
)

echo Building Velopack
cd %~dp0..\..\src\Rust
cargo build --features windows
cd %~dp0..\..\
dotnet build
cd %~dp0

set "version=%~1"
set "releasesDir=%~dp0releases"

:: Write to Const.cs
echo class Const { public const string RELEASES_DIR = @"%releasesDir%"; } > "%~dp0Const.cs"
echo Const.cs file updated with releases directory (%releasesDir%).

echo Compiling AvaloniaCrossPlatTest with dotnet...
dotnet publish -c Release --no-self-contained -r win-x64 -o %~dp0publish

echo class Const { public const string RELEASES_DIR = @"{REPLACE_ME}"; } > "%~dp0Const.cs"
echo Const.cs file reset

echo Building Velopack Release v%version%
%~dp0..\..\build\Debug\net8.0\vpk.exe pack -u AvaloniaCrossPlatTest -v %version% -o %releasesDir% -r win-x64 -e AvaloniaCrossPlat.exe -p %~dp0publish -c win