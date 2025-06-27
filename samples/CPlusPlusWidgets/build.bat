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
if errorlevel 1 exit /b 1

echo.
echo Compiling VelopackCppWidgets with cmake...
cmake -S . -B build-rel
if errorlevel 1 exit /b 1

cmake --build build-rel -j --config Release
if errorlevel 1 exit /b 1

echo.
echo Building Velopack Release v%version%
vpk pack -u VelopackCppWidgets -o %~dp0releases -p %~dp0build-rel\Release -e main.exe -v %*
if errorlevel 1 exit /b 1