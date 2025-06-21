@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version] [extra_args...]
    exit /b 1
)

cd %~dp0




echo.
echo Installing dependencies
uv sync

echo.
echo Building Python application
uv run pyinstaller --onedir --windowed --noconfirm main.py

echo.
echo Building Velopack Release v%~1
mkdir publish 2>nul
xcopy /E /I /Y dist\main\* publish\ >nul
vpk pack --packId VelopackPythonSampleApp --mainExe main.exe -o releases --packDir publish --packVersion %*

echo.
echo Cleaning up
rmdir /S /Q build 2>nul
rmdir /S /Q dist 2>nul
rmdir /S /Q publish 2>nul
del /Q main.spec 2>nul

