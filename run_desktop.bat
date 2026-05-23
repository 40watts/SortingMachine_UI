@echo off
setlocal
cd /d "%~dp0"

if not exist "desktop_v2\bin\TriCellPilot.exe" (
  call build_desktop_v2.bat
)

start "" "desktop_v2\bin\TriCellPilot.exe"
