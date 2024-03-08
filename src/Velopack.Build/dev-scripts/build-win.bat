@echo off

setlocal enabledelayedexpansion

echo.
echo Kill existing MSBuild processes
taskkill /F /IM MSBuild.exe

echo.
echo Kill existing dotnet processes
taskkill /F /IM dotnet.exe

echo.
echo Building Velopack.Build
cd %~dp0..\..\..\
dotnet build -c Debug src/Velopack.Build/Velopack.Build.csproj
