@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling VeloWpfSample with dotnet...
dotnet publish -c Release -o %~dp0publish

echo.
echo Building Velopack Release v%version%
vpk pack -u VeloWpfSample -v %version% -o %~dp0releases -p %~dp0publish -f net48-x64-desktop