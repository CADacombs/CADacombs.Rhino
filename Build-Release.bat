@echo off
echo =======================================
echo Building CADacombs.Rhino (RELEASE)
echo =======================================

:: 1. Clean the previous builds to ensure no stale files[cite: 12]
dotnet clean CADacombs\CADacombs.csproj --configuration Release[cite: 12]

:: 2. Compile the Release build[cite: 12]
dotnet build CADacombs\CADacombs.csproj --configuration Release[cite: 12]

:: Check if the build failed and abort if it did[cite: 12]
if %ERRORLEVEL% neq 0 (
    echo.
    echo =======================================
    echo BUILD FAILED! Aborting Yak Packaging.[cite: 12]
    echo =======================================
    pause
    exit /b %ERRORLEVEL%
)

:: 3. Navigate to the output folder where the framework subfolders were created[cite: 12]
cd CADacombs\bin\Release[cite: 12]

:: 4. Copy shared assets to the root of the Release folder so Yak packages them correctly[cite: 12]
copy net48\manifest.yml .[cite: 12]
copy net48\CADacombs.rui .[cite: 12]
copy net48\icon.png .[cite: 12]

if %ERRORLEVEL% neq 0 (
    echo.
    echo =======================================
    echo ASSET COPY FAILED! Aborting Yak Packaging.[cite: 12]
    echo =======================================
    cd ..\..\..[cite: 12]
    pause
    exit /b %ERRORLEVEL%
)

:: 5. Command Yak to build the package[cite: 12]
echo.
echo =======================================
echo Creating Yak Package...[cite: 12]
echo =======================================
"C:\Program Files\Rhino 8\System\Yak.exe" build[cite: 12]

:: Return to the root folder[cite: 12]
cd ..\..\..[cite: 12]

echo.
echo =======================================
echo YAK PACKAGE CREATED SUCCESSFULLY!
echo =======================================
set /p OpenFolder="Open Release folder in Explorer? [Y/n]: "
:: Default to Y if the user just presses Enter
if /i "%OpenFolder%"=="" set OpenFolder=Y
if /i "%OpenFolder%"=="Y" explorer "CADacombs\bin\Release"

echo.
pause[cite: 12]