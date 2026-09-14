@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0S22-T02-GUI.ps1"
if errorlevel 1 pause
