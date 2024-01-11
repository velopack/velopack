@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

echo Compiling VeloWpfSample with dotnet...
dotnet publish -c Release --no-self-contained -r win-x64 -o %~dp0publish

echo Building Velopack Release v%version%
vpk pack -u VeloWpfSample -v %version% -o %~dp0releases -p %~dp0publish -f net8-x64-desktop