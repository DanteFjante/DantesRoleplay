@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-mcp-server.ps1" -Update %*
if errorlevel 1 (
  echo.
  echo Update did not complete. See the explanation above.
  pause
  exit /b 1
)
