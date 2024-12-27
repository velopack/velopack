@echo off
setlocal enabledelayedexpansion
cd %~dp0..
%~dp0..\..\..\build\Debug\net8.0\vpk publish -o releases %*