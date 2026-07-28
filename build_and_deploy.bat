@echo off
echo Building Tower Stats Mod...
cd /d "%~dp0"
dotnet build TowerStatsMod.csproj -c Release
if %ERRORLEVEL% NEQ 0 goto BUILD_FAILED

echo.
echo Build SUCCESSFUL! Deploying plugin...

set DEST_R2=C:\Users\luah8\AppData\Roaming\r2modmanPlus-local\SineusArenaSurvivors\profiles\Default\BepInEx\plugins\TowerStatsMod
if not exist "%DEST_R2%" mkdir "%DEST_R2%"
copy /Y "bin\Release\netstandard2.1\TowerStatsMod.dll" "%DEST_R2%\TowerStatsMod.dll"
echo Deployed to r2modman profile: %DEST_R2%\TowerStatsMod.dll

set DEST_GAME=..\BepInEx\plugins\TowerStatsMod
if exist "..\BepInEx\plugins" (
    if not exist "%DEST_GAME%" mkdir "%DEST_GAME%"
    copy /Y "bin\Release\netstandard2.1\TowerStatsMod.dll" "%DEST_GAME%\TowerStatsMod.dll"
    echo Deployed to Game BepInEx folder: %DEST_GAME%\TowerStatsMod.dll
)

echo.
echo ========================================================
echo  Tower Stats Mod deployed successfully!
echo  Start the game via r2modman to enjoy Kills and DPS stats.
echo ========================================================
goto END

:BUILD_FAILED
echo.
echo Build FAILED. Check errors above.

:END
