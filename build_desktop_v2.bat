@echo off
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe introuvable.
  exit /b 1
)

set FRAMEWORK=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FRAMEWORK%\WPF
set APPDIR=%~dp0desktop_v2\app
set OUTDIR=%~dp0desktop_v2\bin

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
if not exist "%OUTDIR%\data" mkdir "%OUTDIR%\data"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$target = [System.IO.Path]::GetFullPath('%OUTDIR%\TriCellPilot.exe'); $running = @(); foreach ($p in Get-Process) { if ($p.ProcessName -eq 'TriCellPilot' -or $p.ProcessName -eq 'SortingMachineDesktop') { try { if ($p.Path -and ([System.IO.Path]::GetFullPath($p.Path) -ieq $target)) { $running += $p } } catch {} } }; if ($running.Count -gt 0) { foreach ($p in $running) { Write-Host ('TriCell Pilot est encore ouvert: PID ' + $p.Id + ' depuis ' + $p.StartTime + '. Fermer l''application atelier avant de reconstruire ' + $target + '.'); }; exit 10 }"
if errorlevel 1 exit /b 1

pushd "%APPDIR%"
  "%CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 /out:"%OUTDIR%\TriCellPilot.exe" /win32icon:"%APPDIR%\app.ico" ^
    /r:%WPF%\WindowsBase.dll ^
    /r:%WPF%\PresentationCore.dll ^
    /r:%WPF%\PresentationFramework.dll ^
    /r:%FRAMEWORK%\System.Xaml.dll ^
    /r:%FRAMEWORK%\System.Web.Extensions.dll ^
    /r:%FRAMEWORK%\System.Runtime.Serialization.dll ^
    /r:%FRAMEWORK%\System.Drawing.dll ^
    /r:"%APPDIR%\lib\Microsoft.Web.WebView2.Core.dll" ^
    /r:"%APPDIR%\lib\Microsoft.Web.WebView2.Wpf.dll" ^
    *.cs
popd

if errorlevel 1 exit /b 1

xcopy /Y /I "%APPDIR%\lib\*.dll" "%OUTDIR%\" >nul
copy /Y "%APPDIR%\app.ico" "%OUTDIR%\app.ico" >nul
copy /Y "%OUTDIR%\TriCellPilot.exe" "%OUTDIR%\SortingMachineDesktop.exe" >nul
if exist "%~dp0desktop_app\assets" (
  copy /Y "%APPDIR%\app.ico" "%~dp0desktop_app\assets\app.ico" >nul
)

if exist "%OUTDIR%\web" rmdir /S /Q "%OUTDIR%\web"
xcopy /Y /I /E "%APPDIR%\web" "%OUTDIR%\web" >nul

if exist "%~dp0desktop_app\bin\data" (
  if not exist "%OUTDIR%\data\config.json" (
    xcopy /Y /I /E "%~dp0desktop_app\bin\data\*" "%OUTDIR%\data\" >nul
  )
)

if not exist "%OUTDIR%\data\config.json" (
  echo {}>"%OUTDIR%\data\config.json"
)

if not exist "%OUTDIR%\data\business.json" (
  echo {}>"%OUTDIR%\data\business.json"
)

echo V2 build OK: "%OUTDIR%\TriCellPilot.exe"
