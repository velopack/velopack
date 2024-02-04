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
echo Building Velopack Rust
cd %~dp0..\..\src\Rust
cargo build --features windows

echo.
echo Building Velopack Vpk
cd %~dp0..\..\
dotnet build src/Velopack.Vpk/Velopack.Vpk.csproj

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
echo Building VeloCppWinSample
cd %~dp0
"%MSBUILD_PATH%" VeloCppWinSample.sln /p:Configuration=Release

echo #define UPDATE_URL "REPLACE_ME" > constants.h

echo.
echo Building Velopack Release v%~1
%~dp0..\..\build\Debug\net8.0\vpk pack -u VeloCppWinSample -o releases -p x64\Release -f net8-x64-desktop -v %*