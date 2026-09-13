@echo off
rem The wrapper works with Windows' default PowerShell execution policy.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-mcp-server.ps1" %*
if errorlevel 1 (
  echo.
  echo Startup failed. See the message above.
  pause
  exit /b 1
)
