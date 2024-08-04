@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling SigningSample with dotnet...
dotnet publish -c Release -o %~dp0publish

echo.
echo Building Velopack Release v%version%
vpk pack -u SigningSample -v %version% -o %~dp0releases -p %~dp0publish

echo.
echo "Signing releases.win.json, ensure you have created the keys"
%~dp0../ReleaseSigner/bin/Release/net8.0/ReleaseSigner.exe sign rsa %~dp0releases/releases.win.json --rsa-file %~dp0../keys/rsa.pem