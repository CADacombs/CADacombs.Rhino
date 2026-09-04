@echo off
echo =======================================
echo Building CADacombs.Rhino (DEBUG)
echo =======================================

:: 1. Clean the previous builds to ensure no stale files
dotnet clean CADacombs\CADacombs.csproj --configuration Debug

:: 2. Compile the Debug build
dotnet build CADacombs\CADacombs.csproj --configuration Debug

:: Check if the build failed and abort if it did
if %ERRORLEVEL% neq 0 (
    echo.
    echo =======================================
    echo BUILD FAILED! 
    echo =======================================
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo =======================================
echo BUILD SUCCESSFUL!
echo =======================================
set /p OpenFolder="Open output folder in Explorer? [Y/n]: "
:: Default to Y if the user just presses Enter
if /i "%OpenFolder%"=="" set OpenFolder=Y
if /i "%OpenFolder%"=="Y" explorer "CADacombs\bin\Debug\net8.0"

echo.
pause