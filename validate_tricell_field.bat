@echo off
setlocal
cd /d "%~dp0"

set DURATION=%~1
if "%DURATION%"=="" set DURATION=180

echo TriCell Pilot - validation terrain lecture seule
echo.
echo Ce lanceur ne clique pas DEMARRER et n'envoie aucune commande machine.
echo Il demarre seulement la surveillance avant l'essai operateur.
echo.
echo Duree: %DURATION% secondes
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\Start-FieldValidationWatch.ps1" -Root "%~dp0." -DurationSeconds %DURATION%
if errorlevel 1 (
  echo.
  echo Validation terrain interrompue ou refusee.
  pause
  exit /b 1
)

echo.
echo Validation terrain terminee. Lire le rapport field_validation_*.md et remplir/conserver le CSV observations.
echo Si le CSV est complete apres la fin, lancer refresh_tricell_field_result.bat avant check_tricell_field_result.bat.
pause
