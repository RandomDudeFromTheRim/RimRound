@echo off
setlocal enabledelayedexpansion

:: Use the directory this .bat is in as base
set MODDIR=%~dp0
set MODDIR=%MODDIR:~0,-1%

set "SRCDIR=Source\RimRound"
set "RWDIR=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"

echo RimRound build script
echo Script dir: %MODDIR%
echo.

:: Verify this is the RimRound mod root
set "CHECK_ABOUT=%MODDIR%\About\About.xml"
if not exist "!CHECK_ABOUT!" (
  echo ERROR: Can't find About\About.xml
  echo Make sure build.bat is in RimWorld\Mods\RimRound\
  pause
  exit /b
)

set "CHECK_SRC=%MODDIR%\Source\RimRound\RimRound"
if not exist "!CHECK_SRC!" (
  echo ERROR: Can't find Source\RimRound\RimRound
  pause
  exit /b
)

:: Check for csc
where csc >nul 2>nul
if errorlevel 1 (
  echo ERROR: csc.exe not found. Run from Developer Command Prompt.
  pause
  exit /b
)

:: Check RimWorld DLLs
set "ASMDLL=%RWDIR%\Assembly-CSharp.dll"
if not exist "!ASMDLL!" (
  echo ERROR: Can't find Assembly-CSharp.dll
  echo Looked in: %RWDIR%
  pause
  exit /b
)

:: Gather source files
set SOURCES=
for /r "%MODDIR%\%SRCDIR%\RimRound" %%f in (*.cs) do (
  echo %%f | findstr "AssemblyInfo" >nul
  if errorlevel 1 set SOURCES=!SOURCES! "%%f"
)
for /r "%MODDIR%\%SRCDIR%\FeedingTube" %%f in (*.cs) do (
  set SOURCES=!SOURCES! "%%f"
)
echo Found source files.

echo Building...
csc -target:library -out:"%MODDIR%\Assemblies\RimRound.dll" -noconfig ^
  -define:DEBUG;TRACE -debug:portable -optimize- ^
  /reference:"!ASMDLL!" ^
  /reference:"%RWDIR%\Assembly-CSharp-firstpass.dll" ^
  /reference:"%RWDIR%\UnityEngine.dll" ^
  /reference:"%RWDIR%\UnityEngine.CoreModule.dll" ^
  /reference:"%RWDIR%\UnityEngine.IMGUIModule.dll" ^
  /reference:"%RWDIR%\UnityEngine.TextRenderingModule.dll" ^
  /reference:"%RWDIR%\UnityEngine.InputLegacyModule.dll" ^
  /reference:"%MODDIR%\Dev\harmony16\Current\Assemblies\0Harmony.dll" ^
  /reference:"%MODDIR%\Dev\har\1.6\Assemblies\AlienRace.dll" ^
  /reference:"%MODDIR%\Dev\PipeSystem\1.6\Assemblies\PipeSystem.dll" ^
  /reference:"%MODDIR%\Dev\VEF\1.6\Assemblies\VEF.dll"

set "OUTDLL=%MODDIR%\Assemblies\RimRound.dll"
set "OUTDLL2=%MODDIR%\1.6\Assemblies\RimRound.dll"
if %ERRORLEVEL% equ 0 (
  copy /y "!OUTDLL!" "!OUTDLL2!" >nul
  echo ============================
  echo  BUILD SUCCESSFUL
  echo ============================
) else (
  echo ============================
  echo  BUILD FAILED  (code: %ERRORLEVEL%)
  echo ============================
)
echo.
pause
