@echo off
setlocal
cd /d "%~dp0"

call "%~dp0test_desktop_v2_no_machine.bat"
if errorlevel 1 exit /b 1

if /I not "%TRICELL_ALLOW_APP_SMOKE%"=="1" (
  echo.
  echo App/API smoke tests skipped by default.
  echo Set TRICELL_ALLOW_APP_SMOKE=1 to run FieldValidationWatcherRegression and ApiSmokeCheck.
  echo These optional checks can start desktop_v2\bin\TriCellPilot.exe.
  echo Desktop v2 quality tests OK ^(no-machine default^).
  exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\FieldValidationWatcherRegression.ps1" -Root "%~dp0."
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\ApiSmokeCheck.ps1" -Root "%~dp0."
if errorlevel 1 exit /b 1

echo Desktop v2 quality tests OK.
