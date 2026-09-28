@echo off
rem ═══════════════════════════════════════════════════════════════════════════
rem  BrowserForWP — build the solution inside the Windows guest.
rem
rem  Run from the macOS host with:
rem
rem    prlctl exec "<VM-ID|name>" "cmd.exe" "/c" \
rem        "C:\Mac\Home\Documents\BrowserForWP\tools\vm-build.cmd"
rem
rem  Note the quoting: prlctl exec takes the command and its arguments as
rem  SEPARATE argv entries. Passing "cmd /c ver" as one string fails silently,
rem  which is why this is a batch file rather than a one-liner.
rem
rem  Extra args are forwarded to MSBuild, e.g.
rem    tools\vm-build.cmd /p:Configuration=Release
rem ═══════════════════════════════════════════════════════════════════════════

setlocal

set "REPO=C:\Mac\Home\Documents\BrowserForWP"
set "MSB=C:\Program Files (x86)\MSBuild\12.0\Bin\MSBuild.exe"
set "CFG=Debug"
set "PLAT=ARM"

cd /d "%REPO%" || (echo CANNOT_CD_TO_REPO & exit /b 1)

echo === MSBuild ===
"%MSB%" /version /nologo

echo === Toolchain check ===
if exist "%MSB%" (echo msbuild12 OK) else (echo msbuild12 MISSING)
if exist "C:\Program Files (x86)\Microsoft SDKs\Windows Phone\v8.1" (echo wp81sdk OK) else (echo wp81sdk MISSING)
if exist "C:\Program Files (x86)\Windows Kits\8.1" (echo win81sdk OK) else (echo win81sdk MISSING)

echo === Building BrowserForWP.sln /p:Configuration=%CFG% /p:Platform=%PLAT% ===
"%MSB%" BrowserForWP.sln /nologo /v:minimal /p:Configuration=%CFG% /p:Platform=%PLAT% %*

set "RC=%ERRORLEVEL%"
echo === BUILD_EXIT=%RC% ===

endlocal & exit /b %RC%
