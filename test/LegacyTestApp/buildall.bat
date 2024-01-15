rmdir /s /q bin
rmdir /s /q obj
rmdir /s /q releases

powershell -executionpolicy bypass ./BuildSquirrelWin.ps1
copy /Y Releases\Setup.exe ..\fixtures\LegacyTestApp-SquirrelWinV2-Setup.exe

rmdir /s /q bin
rmdir /s /q obj
rmdir /s /q releases

powershell -executionpolicy bypass ./BuildClowdV2.ps1
copy /Y releases\LegacyTestAppSetup.exe ..\fixtures\LegacyTestApp-ClowdV2-Setup.exe

rmdir /s /q bin
rmdir /s /q obj
rmdir /s /q releases

powershell -executionpolicy bypass ./BuildClowdV3.ps1
copy /Y releases\LegacyTestAppSetup.exe ..\fixtures\LegacyTestApp-ClowdV3-Setup.exe

rmdir /s /q bin
rmdir /s /q obj
rmdir /s /q releases

powershell -executionpolicy bypass ./BuildVelopack0084.ps1
copy /Y releases\LegacyTestApp-win-Setup.exe ..\fixtures\LegacyTestApp-Velopack0084-Setup.exe