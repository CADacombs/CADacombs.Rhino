@echo off
echo =======================================
echo Building CADacombs.Rhino (RELEASE)
echo =======================================

:: 1. Clean the previous builds to ensure no stale files
dotnet clean CADacombs\CADacombs.csproj --configuration Release

:: 2. Compile the Release build
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

:: 3. Navigate to the output folder where the framework subfolders were created
cd CADacombs\bin\Release

:: 4. Copy shared assets to the root of the Release folder so Yak packages them correctly
copy net48\manifest.yml .
copy net48\CADacombs.rui .
copy net48\icon.png .

if %ERRORLEVEL% neq 0 (
    echo.
    echo =======================================
    echo ASSET COPY FAILED! Aborting Yak Packaging.
    echo =======================================
    cd ..\..\..
    pause
    exit /b %ERRORLEVEL%
)

:: 5. Command Yak to build the package
echo.
echo =======================================
echo Creating Yak Package...
echo =======================================
"C:\Program Files\Rhino 8\System\Yak.exe" build

:: Return to the root folder
cd ..\..\..

echo.
pause