@echo off
setlocal
set "S9_DIAG_PACKET=%~dp0"
powershell.exe -NoProfile -Command "& ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $env:S9_DIAG_PACKET 'Run-Network-Diagnostic.ps1')))) -PacketRoot $env:S9_DIAG_PACKET"
pause
