@echo off
rem ============================================================================
rem  Build a runnable folder for osu! corn.
rem  Default target: D:\osu-corn-<VERSION>-local   (VERSION is read from VERSION)
rem
rem    pack.cmd                 -> D:\osu-corn-<VERSION>-local
rem    pack.cmd D:\some\folder  -> any other folder
rem
rem  Two steps on purpose: publish inside the workspace first, then copy out.
rem  Publishing straight into a fresh folder on D:\ fails with
rem  "UnauthorizedAccessException: Access to the path '<target>\OsuCorn.exe'".
rem
rem  NOTE: keep this file ASCII-only. cmd parses it in the OEM codepage; a
rem  non-ASCII character in a comment breaks the whole script.
rem ============================================================================
setlocal
set "ROOT=%~dp0"
set "TARGET=%~1"
if defined TARGET goto target_ready
set "VER="
if exist "%ROOT%VERSION" set /p VER=<"%ROOT%VERSION"
if not defined VER set "VER=dev"
set "TARGET=D:\osu-corn-%VER%-local"
:target_ready
set "STAGE=%ROOT%bin\local-publish"
set "LOG=%ROOT%bin\pack.log"

set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=D:\AI\tools\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

set "TMPDIR=%ROOT%bin\tmp"
if not exist "%TMPDIR%" mkdir "%TMPDIR%"
set "TMP=%TMPDIR%"
set "TEMP=%TMPDIR%"

echo ================================================================
echo  target : %TARGET%
echo  stage  : %STAGE%
echo  log    : %LOG%
echo ================================================================
echo.

if exist "%STAGE%" rmdir /s /q "%STAGE%"

echo [1/3] publishing (about a minute)...
"%DOTNET%" publish "%ROOT%OsuCorn.csproj" -c Release -r win-x64 --self-contained true ^
 -p:PublishSingleFile=true -o "%STAGE%" --nologo > "%LOG%" 2>&1
if errorlevel 1 (
    echo [FAIL] publish failed. Last lines of the log:
    powershell -NoProfile -Command "Get-Content -LiteralPath '%LOG%' -Tail 25"
    goto fail
)

echo [2/3] copying to the target...
if exist "%TARGET%" rmdir /s /q "%TARGET%" 2>nul
robocopy "%STAGE%" "%TARGET%" /e /nfl /ndl /njh /njs /np >nul
if errorlevel 8 (
    echo   robocopy failed, trying xcopy...
    xcopy "%STAGE%" "%TARGET%" /e /i /y /q >nul
    if errorlevel 1 (
        echo [FAIL] xcopy failed too. Is "%TARGET%" writable?
        goto fail
    )
)

if exist "%STAGE%\OsuCorn.pdb" del /q "%STAGE%\OsuCorn.pdb"
if exist "%TARGET%\OsuCorn.pdb" del /q "%TARGET%\OsuCorn.pdb"
if not exist "%TARGET%\presets.json" copy /y "%ROOT%presets.json" "%TARGET%\presets.json" >nul

echo [3/3] verifying...
if not exist "%TARGET%\OsuCorn.exe" (
    echo [FAIL] "%TARGET%\OsuCorn.exe" is missing
    goto fail
)

echo [OK] done. Contents of "%TARGET%":
dir /b "%TARGET%"
echo.
echo Run it: "%TARGET%\OsuCorn.exe"
echo.
goto end

:fail
echo.
pause
endlocal
exit /b 1

:end
pause
endlocal
exit /b 0
