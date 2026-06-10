param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$BaseUrl = "http://127.0.0.1:8050",
    [int]$DurationSeconds = 180,
    [int]$PollMilliseconds = 500,
    [int]$MinAcceptedTops = 9,
    [int]$MinCounterDelta = 9,
    [int]$MinPhysicalObservations = 9,
    [string]$RequiredGoodLanes = "1,2,3,4,5,6,7,8,9",
    [string]$ObservationCsvPath = "",
    [string]$OutputPath = "",
    [switch]$AllowAlreadyRunning
)

$ErrorActionPreference = "Stop"

function Invoke-Json {
    param([string]$Path)
    return Invoke-RestMethod -Uri "$BaseUrl$Path" -TimeoutSec 5
}

function Wait-Api {
    param([int]$Seconds = 20)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            return Invoke-Json "/api/state"
        } catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)

    throw "API locale indisponible apres $Seconds secondes."
}

$rootPath = (Resolve-Path $Root).Path
$desktop = Join-Path $rootPath "desktop_v2"
$exe = Join-Path $desktop "bin\TriCellPilot.exe"
$watcher = Join-Path $desktop "tools\Watch-TriCellFieldValidation.ps1"

if (-not (Test-Path $exe)) {
    throw "Executable TriCell Pilot introuvable: $exe"
}

if (-not (Test-Path $watcher)) {
    throw "Watcher validation terrain introuvable: $watcher"
}

try {
    $state = Invoke-Json "/api/state"
} catch {
    Write-Host "API TriCell Pilot indisponible: lancement de l'application, sans commande machine."
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden | Out-Null
    $state = Wait-Api 20
}

$readiness = Invoke-Json "/api/diagnostic/start-readiness"
$routing = Invoke-Json "/api/diagnostic/physical-routing"

Write-Host "Etat TriCell Pilot avant surveillance:"
Write-Host ("- Connecte: {0}" -f $state.Connected)
Write-Host ("- Resultat: {0}" -f $state.Live.Result)
Write-Host ("- Lot: #{0} {1}, controle={2}" -f $state.Production.CurrentLotId, $state.Production.LotStatus, $state.Production.LotControlEnabled)
Write-Host ("- ReadyToStart: {0}" -f $readiness.startReadiness.ReadyToStart)
Write-Host ("- Ligne attendue/appliquee: {0} / {1}" -f $routing.physicalRouting.ExpectedLane, $routing.physicalRouting.AppliedLane)
Write-Host ("- Couverture voies GOOD requise: {0}" -f $RequiredGoodLanes)

if ([bool]$state.Production.LotControlEnabled -and -not $AllowAlreadyRunning) {
    throw "Validation non lancee: le lot semble deja en cycle. Lancer la surveillance avant DEMARRER pour obtenir une preuve START complete, ou utiliser -AllowAlreadyRunning pour un diagnostic lecture seule."
}

if (-not [bool]$state.Connected) {
    throw "Validation non lancee: TriCell Pilot n'est pas connecte a la machine."
}

& powershell -NoProfile -ExecutionPolicy Bypass -File $watcher `
    -BaseUrl $BaseUrl `
    -DurationSeconds $DurationSeconds `
    -PollMilliseconds $PollMilliseconds `
    -MinAcceptedTops $MinAcceptedTops `
    -MinCounterDelta $MinCounterDelta `
    -MinPhysicalObservations $MinPhysicalObservations `
    -RequiredGoodLanes $RequiredGoodLanes `
    -ObservationCsvPath $ObservationCsvPath `
    -OutputPath $OutputPath

if ($LASTEXITCODE -ne 0) {
    throw "La surveillance terrain s'est terminee avec le code $LASTEXITCODE."
}
