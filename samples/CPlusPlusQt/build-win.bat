@echo off
setlocal

rem Find the absolute path of the script
set "SCRIPT_DIR=%~dp0"

rem Check if version and Qt path parameters are provided
if "%~2"=="" (
    echo Version number and path to Qt installation are required.
    echo Usage: %~nx0 [version] [path-to-qt]
    echo.
    echo Example: %~nx0 1.0.4 C:\Users\kalle\Qt\6.5.3\msvc2019_64
    echo.
    exit /b 1
)

set BUILD_VERSION=%~1
set QT_DIR=%~2

set BUILD_DIR=%SCRIPT_DIR%build
set RELEASE_DIR=%SCRIPT_DIR%releases
set PUBLISH_DIR=%SCRIPT_DIR%publish

echo.
echo Compiling Velopack Qt sample...

echo #define UPDATE_URL R"(%RELEASE_DIR%)" > constants.h

rem Remove build directory if it exists
if exist "%BUILD_DIR%" (
    rmdir /s /q "%BUILD_DIR%"
)

rem Create build directory to run cmake in
echo Creating %BUILD_DIR%...
mkdir "%BUILD_DIR%"

rem Navigate to the build directory
cd /d "%BUILD_DIR%" || exit /b

cmake -G"Ninja" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="%PUBLISH_DIR%" -DCMAKE_PREFIX_PATH="%QT_DIR%" ..

if %errorlevel% neq 0 (
    echo I couldn't run cmake. Exiting.
    cd /d "%SCRIPT_DIR%"
    exit /b 1
)

ninja
ninja install

echo.
echo Building Velopack Qt Sample Release v%BUILD_VERSION%
 vpk pack --packId appVelopackQtSample ^
    --mainExe bin\appVelopackQtSample.exe ^
    --packTitle VelopackQtSample ^
    -v %BUILD_VERSION% ^
    -o "%RELEASE_DIR%" ^
    -p "%PUBLISH_DIR%"

:end
