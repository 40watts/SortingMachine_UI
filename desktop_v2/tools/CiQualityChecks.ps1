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
$bin = Join-Path $desktop "bin"

Assert-True (Test-Path $csc) "csc.exe introuvable: $csc"

& (Join-Path $rootPath "build_desktop_v2.bat")
if ($LASTEXITCODE -ne 0) {
    throw "build_desktop_v2.bat a echoue avec le code $LASTEXITCODE"
}

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

function Invoke-Regression {
    param(
        [string]$MainType,
        [string]$OutputName,
        [string]$RegressionSource
    )

    $appSources = Get-ChildItem -Path (Join-Path $desktop "app") -Filter "*.cs" -File |
        ForEach-Object { $_.FullName }
    $output = Join-Path $bin $OutputName
    $arguments = @(
        "/nologo",
        "/codepage:65001",
        "/target:exe",
        "/platform:x64",
        "/main:$MainType",
        "/out:$output"
    ) + $refs + $appSources + (Join-Path $desktop $RegressionSource)

    & $csc @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$OutputName compilation echouee avec le code $LASTEXITCODE"
    }

    & $output
    if ($LASTEXITCODE -ne 0) {
        throw "$OutputName regression echouee avec le code $LASTEXITCODE"
    }
}

Invoke-Regression `
    -MainType "SortingMachineDesktop.RoutingLedgerRegression" `
    -OutputName "RoutingLedgerRegression.exe" `
    -RegressionSource "tools\RoutingLedgerRegression.cs"

Invoke-Regression `
    -MainType "SortingMachineDesktop.QualityIntervalRoutingRegression" `
    -OutputName "QualityIntervalRoutingRegression.exe" `
    -RegressionSource "tools\QualityIntervalRoutingRegression.cs"

& (Join-Path $desktop "tools\StaticQualityChecks.ps1") -Root $rootPath
& (Join-Path $desktop "tools\RepositoryPreflight.ps1") -Root $rootPath

Write-Host "CiQualityChecks OK"
