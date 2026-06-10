@echo off
setlocal
cd /d "%~dp0"

set REPORT=%~1

echo TriCell Pilot - verification du rapport terrain et du lot courant
echo.

if "%REPORT%"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\Assert-FieldValidationReport.ps1" -Root "%~dp0."
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\Assert-FieldValidationReport.ps1" -Root "%~dp0." -ReportPath "%REPORT%"
)

if errorlevel 1 (
  echo.
  echo Rapport terrain non valide ou incomplet.
  pause
  exit /b 1
)

echo.
echo Rapport terrain valide: les verdicts trace, compteurs, observation, couverture voies GOOD sont OK, et le lot courant correspond.
pause
