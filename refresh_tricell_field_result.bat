@echo off
setlocal
cd /d "%~dp0"

set REPORT=%~1

echo TriCell Pilot - actualisation du rapport terrain depuis le CSV
echo.
echo Ce lanceur relit le rapport source et le CSV d'observations.
echo Il n'envoie aucune commande machine.
echo.

if "%REPORT%"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\Update-FieldValidationReportFromCsv.ps1" -Root "%~dp0."
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\Update-FieldValidationReportFromCsv.ps1" -Root "%~dp0." -ReportPath "%REPORT%"
)

if errorlevel 1 (
  echo.
  echo Actualisation du rapport terrain refusee ou incomplete.
  pause
  exit /b 1
)

echo.
echo Rapport actualise. Lancer ensuite check_tricell_field_result.bat.
pause
