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
if errorlevel 1 exit /b 1

echo.
echo Cleaning lib-nodejs
call npm run clean
if errorlevel 1 exit /b 1

echo.
echo Compiling lib-nodejs
call npm run dev
if errorlevel 1 exit /b 1

echo.
echo Packing lib-nodejs
call npm pack
if errorlevel 1 exit /b 1

cd %~dp0

echo.
echo Running npm install
call npm install
if errorlevel 1 exit /b 1

echo.
echo Installing lib-nodejs package
call npm install ..\..\src\lib-nodejs\velopack-0.0.0.tgz
if errorlevel 1 exit /b 1

echo.
echo Packing with electron-forge
call npm run package
if errorlevel 1 exit /b 1

echo.
echo Creating Velopack Release
call vpk pack -u VelopackElectronSample -v %version% -o %~dp0releases -p %~dp0out\VelopackElectronSample-win32-x64
if errorlevel 1 exit /b 1