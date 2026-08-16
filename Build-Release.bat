@echo off
echo =======================================
echo Building CADacombs.Rhino (RELEASE)
echo =======================================

:: 1. Compile the Release build
dotnet build CADacombs\CADacombs.csproj --configuration Release

:: Check if the build failed and abort if it did
if %ERRORLEVEL% neq 0 (
    echo.
    echo =======================================
    echo BUILD FAILED! Aborting Yak Packaging.
    echo =======================================
    pause
    exit /b %ERRORLEVEL%
)

:: 2. Navigate to the output folder where all the files were copied
cd CADacombs\bin\Release

:: 3. Command Yak to build the package
echo.
echo =======================================
echo Creating Yak Package...
echo =======================================
"C:\Program Files\Rhino 8\System\Yak.exe" build

:: 4. Return to the root folder
cd ..\..\..

echo.
pause