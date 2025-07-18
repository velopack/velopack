@echo off
REM This script requires several tools to be installed for it to work:
REM cargo (rust): winget install Rustlang.Rustup
REM Nerdbank.GitVersioning (nbgv): dotnet tool install --global nbgv
REM C++ Build Tools, typically installed via "Desktop development with C++" workload.

setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version] [extra_args...]
    exit /b 1
)

cd %~dp0..\..\..\

echo.
echo Building Velopack Rust
cargo build --features windows
if errorlevel 1 exit /b 1

echo.
echo Building Velopack Vpk
dotnet build src/vpk/Velopack.Vpk/Velopack.Vpk.csproj
if errorlevel 1 exit /b 1

cd %~dp0..
set "version=%~1"

echo.
echo Compiling VelopackCSharpAvalonia with dotnet...
dotnet publish -c Release --self-contained -r win-x64 -o publish -p:UseLocalVelopack=true
if errorlevel 1 exit /b 1

echo.
echo Building Velopack Release v%version%
%~dp0..\..\..\build\Debug\net8.0\vpk pack -u VelopackCSharpAvalonia -o releases -p publish -v %*
if errorlevel 1 exit /b 1