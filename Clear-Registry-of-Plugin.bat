@echo off
echo =======================================
echo Clearing CADacombs Registry Key...
echo =======================================

@echo on

:: Use the native Windows Registry tool to forcefully delete the key
reg delete "HKCU\Software\McNeel\Rhinoceros\8.0\Plug-ins\274aafc3-84b3-47a6-86af-dac682ff0c84" /f
reg delete "HKCU\Software\McNeel\Rhinoceros\8.0\Global Options\Plug-ins\274aafc3-84b3-47a6-86af-dac682ff0c84" /f

@echo off

echo.
:: echo Registry key cleared!
echo.
pause