@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling CSharpUno with dotnet...
dotnet publish -c Release --framework net9.0-desktop -o %~dp0UnoSample\publish UnoSample\UnoSample.csproj
if errorlevel 1 exit /b 1

echo.
echo Building Velopack Release v%version%
vpk pack -u CSharpUno -v %version% -o %~dp0UnoSample\releases -p %~dp0UnoSample\publish --mainExe UnoSample.exe
if errorlevel 1 exit /b 1
