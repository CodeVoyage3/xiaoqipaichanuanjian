@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-S9T06-100-Bridge.ps1"
set "bridgeExit=%ERRORLEVEL%"
if not "%bridgeExit%"=="0" pause
exit /b %bridgeExit%
