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

echo.
echo Building Velopack Rust
cd %~dp0..\..\..\src\Rust
cargo build --features windows

echo.
echo Building Velopack Vpk
cd %~dp0..\..\..\
dotnet build src/Velopack.Vpk/Velopack.Vpk.csproj

cd %~dp0..
set "version=%~1"

echo.
echo Compiling AvaloniaCrossPlat with dotnet...
dotnet publish -c Release --no-self-contained -r win-x64 -o publish -p:UseLocalVelopack=true

echo.
echo Building Velopack Release v%version%
%~dp0..\..\..\build\Debug\net8.0\vpk pack -u AvaloniaCrossPlat -o releases -p publish -f net8-x64-desktop -v %*