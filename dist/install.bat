@echo off
rem AnLan CAD helper installer wrapper: double-click me to install (no admin needed)
rem Params pass through: install.bat -ApiKey sk-xxx
setlocal
set "PS1=%~dp0install.ps1"
if not exist "%PS1%" (
  echo [ERROR] install.ps1 not found next to install.bat. Extract the whole zip first.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
if errorlevel 1 (
  echo.
  echo [ERROR] Install failed - read the messages above, then press any key to close.
  pause
  exit /b 1
)
echo.
echo Install finished. You can close this window.
pause
exit /b 0
