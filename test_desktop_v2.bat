@echo off
setlocal
cd /d "%~dp0"

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe introuvable.
  exit /b 1
)

call "%~dp0build_desktop_v2.bat"
if errorlevel 1 exit /b 1

set FRAMEWORK=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FRAMEWORK%\WPF

pushd "%~dp0desktop_v2"
  set REFS=/r:%WPF%\WindowsBase.dll /r:%WPF%\PresentationCore.dll /r:%WPF%\PresentationFramework.dll /r:%FRAMEWORK%\System.Xaml.dll /r:%FRAMEWORK%\System.Web.Extensions.dll /r:%FRAMEWORK%\System.Runtime.Serialization.dll /r:%FRAMEWORK%\System.Drawing.dll /r:app\lib\Microsoft.Web.WebView2.Core.dll /r:app\lib\Microsoft.Web.WebView2.Wpf.dll

  "%CSC%" /nologo /codepage:65001 /target:exe /platform:x64 /main:SortingMachineDesktop.RoutingLedgerRegression /out:bin\RoutingLedgerRegression.exe %REFS% app\*.cs tools\RoutingLedgerRegression.cs
  if errorlevel 1 exit /b 1
  bin\RoutingLedgerRegression.exe
  if errorlevel 1 exit /b 1

  "%CSC%" /nologo /codepage:65001 /target:exe /platform:x64 /main:SortingMachineDesktop.QualityIntervalRoutingRegression /out:bin\QualityIntervalRoutingRegression.exe %REFS% app\*.cs tools\QualityIntervalRoutingRegression.cs
  if errorlevel 1 exit /b 1
  bin\QualityIntervalRoutingRegression.exe
  if errorlevel 1 exit /b 1
popd

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\StaticQualityChecks.ps1" -Root "%~dp0."
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\RepositoryPreflight.ps1" -Root "%~dp0."
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0desktop_v2\tools\ApiSmokeCheck.ps1" -Root "%~dp0."
if errorlevel 1 exit /b 1

echo Desktop v2 quality tests OK.
