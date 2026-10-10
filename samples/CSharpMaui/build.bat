@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling VelopackCSharpMaui with dotnet...
dotnet publish %~dp0CSharpMaui.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --no-self-contained -o %~dp0publish
if errorlevel 1 exit /b 1

echo.
echo Building Velopack Release v%version%
vpk pack -u VelopackCSharpMaui -v %version% -o %~dp0releases -p %~dp0publish -f net10-x64-desktop
if errorlevel 1 exit /b 1
