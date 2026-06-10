@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\NoMachineQualityChecks.ps1" -Root "%~dp0."
if errorlevel 1 exit /b 1

echo Desktop v2 no-machine quality tests OK.
