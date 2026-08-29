@echo off
rem  Runs the catalog tools, so `roleplay export catalog` is a command that exists.
rem
rem  Every document in this repository -- including the seeded contract
rem  procedure.system.create-feature, which sessions are told to follow literally -- says to run
rem  `roleplay export catalog`. There was no such command: `roleplay` is the assembly name of
rem  DantesRoleplay.Tools, not anything on PATH. An instruction nobody can follow is worse than no
rem  instruction, because it gets guessed at instead of read.
rem
rem  A .cmd rather than a .ps1 on purpose. PowerShell refuses to run unsigned scripts under the
rem  default execution policy, so a .ps1 shim would have replaced one piece of friction with
rem  another -- "first change your execution policy" is not a fix.
rem
rem  Usage:  roleplay export catalog
rem          roleplay validate catalog
rem          roleplay import catalog --dry-run
rem          roleplay verify catalog

setlocal
set "TOOL=%~dp0DantesRoleplay.Tools\bin\Debug\net10.0\roleplay.exe"

if not exist "%TOOL%" (
    echo Building DantesRoleplay.Tools...
    dotnet build "%~dp0DantesRoleplay.Tools\DantesRoleplay.Tools.csproj" -v quiet --nologo
    if errorlevel 1 exit /b 1
)

rem  Arguments forwarded verbatim, and the working directory left alone: the tool finds the
rem  database by walking up from where the caller stands, and a relative catalog path has to mean
rem  what the caller meant by it.
"%TOOL%" %*
exit /b %ERRORLEVEL%
