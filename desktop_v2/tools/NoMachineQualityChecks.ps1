param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

$rootPath = (Resolve-Path $Root).Path
$desktop = Join-Path $rootPath "desktop_v2"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpf = Join-Path $framework "WPF"
$testOut = Join-Path ([System.IO.Path]::GetTempPath()) ("TriCellNoMachine_" + [guid]::NewGuid().ToString("N"))

Assert-True (Test-Path $csc) "csc.exe introuvable: $csc"

& (Join-Path $rootPath "build_desktop_v2.bat")
if ($LASTEXITCODE -ne 0) {
    throw "build_desktop_v2.bat a echoue avec le code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $testOut | Out-Null
Copy-Item -Path (Join-Path $desktop "app\lib\*.dll") -Destination $testOut -Force

$refs = @(
    "/r:$wpf\WindowsBase.dll",
    "/r:$wpf\PresentationCore.dll",
    "/r:$wpf\PresentationFramework.dll",
    "/r:$framework\System.Xaml.dll",
    "/r:$framework\System.Web.Extensions.dll",
    "/r:$framework\System.Runtime.Serialization.dll",
    "/r:$framework\System.Drawing.dll",
    "/r:$desktop\app\lib\Microsoft.Web.WebView2.Core.dll",
    "/r:$desktop\app\lib\Microsoft.Web.WebView2.Wpf.dll"
)

$appSources = Get-ChildItem -Path (Join-Path $desktop "app") -Filter "*.cs" -File |
    ForEach-Object { $_.FullName }

function Invoke-Regression {
    param(
        [string]$MainType,
        [string]$OutputName,
        [string]$RegressionSource,
        [string[]]$Arguments = @()
    )

    $output = Join-Path $testOut $OutputName
    $compilerArgs = @(
        "/nologo",
        "/codepage:65001",
        "/target:exe",
        "/platform:x64",
        "/main:$MainType",
        "/out:$output"
    ) + $refs + $appSources + (Join-Path $desktop $RegressionSource)

    & $csc @compilerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$OutputName compilation echouee avec le code $LASTEXITCODE"
    }

    & $output @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$OutputName regression echouee avec le code $LASTEXITCODE"
    }
}

function Remove-TempTreeBestEffort {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return
    }

    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    for ($attempt = 1; $attempt -le 8; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -eq 8) {
                Write-Warning ("Nettoyage temporaire reporte: {0} ({1})" -f $Path, $_.Exception.Message)
                return
            }

            Start-Sleep -Milliseconds (150 * $attempt)
        }
    }
}

try {
    Invoke-Regression `
        -MainType "SortingMachineDesktop.RoutingLedgerRegression" `
        -OutputName "RoutingLedgerRegression.exe" `
        -RegressionSource "tools\RoutingLedgerRegression.cs"

    Invoke-Regression `
        -MainType "SortingMachineDesktop.QualityIntervalRoutingRegression" `
        -OutputName "QualityIntervalRoutingRegression.exe" `
        -RegressionSource "tools\QualityIntervalRoutingRegression.cs"

    Invoke-Regression `
        -MainType "SortingMachineDesktop.PhysicalRoutingApiRegression" `
        -OutputName "PhysicalRoutingApiRegression.exe" `
        -RegressionSource "tools\PhysicalRoutingApiRegression.cs" `
        -Arguments @((Join-Path $desktop "app\web"))

    Invoke-Regression `
        -MainType "SortingMachineDesktop.NgSweepSimulatorRegression" `
        -OutputName "NgSweepSimulatorRegression.exe" `
        -RegressionSource "tools\NgSweepSimulatorRegression.cs"

    Invoke-Regression `
        -MainType "SortingMachineDesktop.ScannerHandshakeRegression" `
        -OutputName "ScannerHandshakeRegression.exe" `
        -RegressionSource "tools\ScannerHandshakeRegression.cs"

    & (Join-Path $desktop "tools\StaticQualityChecks.ps1") -Root $rootPath
    if ($LASTEXITCODE -ne 0) {
        throw "StaticQualityChecks.ps1 a echoue avec le code $LASTEXITCODE"
    }

    & (Join-Path $desktop "tools\RepositoryPreflight.ps1") -Root $rootPath
    if ($LASTEXITCODE -ne 0) {
        throw "RepositoryPreflight.ps1 a echoue avec le code $LASTEXITCODE"
    }

    Write-Host "NoMachineQualityChecks OK"
}
finally {
    Remove-TempTreeBestEffort -Path $testOut
}
