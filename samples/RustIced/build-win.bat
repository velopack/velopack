@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version] [extra_args...]
    exit /b 1
)

cd %~dp0

echo.
echo Building Iced/Rust
cargo build

echo.
echo Building Velopack Release v%~1
mkdir publish
move target\debug\velorusticedsample.exe publish\velorusticedsample.exe
vpk pack -u VeloRustIcedSample -o releases -p publish -v %*