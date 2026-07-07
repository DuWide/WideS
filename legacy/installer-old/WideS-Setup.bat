@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0WideS-Setup.ps1" -RunAfterInstall
pause
