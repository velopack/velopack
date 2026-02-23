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
if errorlevel 1 exit /b 1

echo.
echo Building Velopack Release v%~1

if not exist publish mkdir publish
if errorlevel 1 exit /b 1

copy /y target\debug\velorusticedsample.exe publish\velorusticedsample.exe
if errorlevel 1 exit /b 1

vpk pack -u VeloRustIcedSample -o releases -p publish -v %*
if errorlevel 1 exit /b 1