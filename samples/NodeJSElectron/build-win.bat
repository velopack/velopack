@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

echo.
echo Cleaning lib-nodejs
cd %~dp0..\..\src\lib-nodejs
call npm run clean

echo.
echo Compiling lib-nodejs
cd %~dp0..\..\src\lib-nodejs
call npm run dev

echo.
echo Packing lib-nodejs
call npm pack

echo.
echo Installing lib-nodejs package
cd %~dp0
call npm install ..\..\src\lib-nodejs\velopack-0.0.0.tgz

echo.
echo Packing with electron-forge
call npm run package

echo.
echo Creating Velopack Release
call vpk pack -u VelopackElectronSample -v %version% -o %~dp0releases -p %~dp0out\VelopackElectronSample-win32-x64



@REM echo.
@REM echo Compiling VelopackCSharpWpf with dotnet...
@REM dotnet publish -c Release -o %~dp0publish
@REM 
@REM echo.
@REM echo Building Velopack Release v%version%
@REM vpk pack -u VelopackCSharpWpf -v %version% -o %~dp0releases -p %~dp0publish -f net8-x64-desktop