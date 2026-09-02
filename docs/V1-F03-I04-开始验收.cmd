@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0V1-F03-I04-GUI.ps1" -Action Enter
if errorlevel 1 (
  echo.
  echo 启动失败，请保留以上输出并反馈；不要手工移动或删除运行目录。
)
pause
