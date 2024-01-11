@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
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
%~dp0..\..\..\build\Debug\net8.0\vpk pack -u AvaloniaCrossPlat -v %version% -o releases -p publish -f net8-x64-desktop