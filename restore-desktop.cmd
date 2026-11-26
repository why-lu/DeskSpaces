@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0restore-icons.ps1"
if errorlevel 1 (
  echo DeskSpaces 恢复失败，请查看窗口错误。
  pause
) else (
  echo 已请求恢复 Windows 桌面图标。
  timeout /t 3 /nobreak >nul
)
