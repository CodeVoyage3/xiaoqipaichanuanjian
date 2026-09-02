@echo off
set "action=Enter"
if /I "%~1"=="Check" set "action=Check"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0V1-F03-I04-GUI.ps1" -Action %action%
if errorlevel 1 echo START_FAILED. Copy the PowerShell error to Codex. Do not move or delete runtime folders.
pause
