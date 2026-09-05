@echo off
setlocal
set "S9T06_PACKET_ROOT=%~dp0"
echo S9-T06 independent Windows 11 synthetic test evidence
echo 1. Init - before first installation
echo 2. Before - close 1.0.0 after creating synthetic data
echo 3. After - close 1.0.1 after online upgrade
echo 4. Uninstalled - after uninstalling; retain test data
choice /c 1234 /n /m "Choose 1-4: "
if errorlevel 4 (set "S9T06_PHASE=Uninstalled") else if errorlevel 3 (set "S9T06_PHASE=After") else if errorlevel 2 (set "S9T06_PHASE=Before") else (set "S9T06_PHASE=Init")
powershell.exe -NoProfile -Command "& ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $env:S9T06_PACKET_ROOT 'Collect-CleanWin11.ps1')))) -Phase $env:S9T06_PHASE -SyntheticOnly -PacketRoot $env:S9T06_PACKET_ROOT"
echo.
echo Keep all evidence files. Do not delete or reset app data on failure.
pause
