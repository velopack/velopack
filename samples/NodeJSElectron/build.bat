@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

cd %~dp0..\..\src\lib-nodejs

echo.
echo Running npm install
call npm install

echo.
echo Cleaning lib-nodejs
call npm run clean

echo.
echo Compiling lib-nodejs
call npm run dev

echo.
echo Packing lib-nodejs
call npm pack

cd %~dp0

echo.
echo Running npm install
call npm install

echo.
echo Installing lib-nodejs package
call npm install ..\..\src\lib-nodejs\velopack-0.0.0.tgz

echo.
echo Packing with electron-forge
call npm run package

echo.
echo Creating Velopack Release
call vpk pack -u VelopackElectronSample -v %version% -o %~dp0releases -p %~dp0out\VelopackElectronSample-win32-x64