@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version] [extra_args...]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling velopack_libc...
cargo build -p velopack_libc

echo.
echo Compiling VelopackCppWidgetsSample with cmake...
cmake -S . -B build-rel
cmake --build build-rel -j --config Release

echo.
echo Building Velopack Release v%version%
vpk pack -u VelopackCppWidgetsSample -o %~dp0releases -p %~dp0build-rel\Release -e main.exe -v %*