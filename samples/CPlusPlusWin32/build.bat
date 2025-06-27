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
echo Building Velopack C Lib with Cargo
cargo build -p velopack_libc
if errorlevel 1 exit /b 1

cd %~dp0

set VSWHERE_PATH="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"

echo.
echo Locating MSBuild using vswhere
for /f "usebackq tokens=*" %%i in (`%VSWHERE_PATH% -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
  set "MSBUILD_PATH=%%i"
  goto :buildCpp
)

:buildCpp
if not defined MSBUILD_PATH (
    echo MSBuild not found, make sure Visual Studio is installed with C++ Build Tools.
    exit /b 1
)

echo #define UPDATE_URL R"(%~dp0releases)" > constants.h

echo.
echo Building CppWin32Sample
cd %~dp0
"%MSBUILD_PATH%" CppWin32Sample.sln
if errorlevel 1 exit /b 1

echo #define UPDATE_URL "REPLACE_ME" > constants.h

echo.
echo Building Velopack Release v%~1
vpk pack -u VelopackCppWin32Sample -o releases -p x64\Debug -v %* -e CppWin32Sample.exe
if errorlevel 1 exit /b 1