param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$BaseUrl = "http://127.0.0.1:8050"
)

$ErrorActionPreference = "Stop"

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Json {
    param([string]$Path)
    return Invoke-RestMethod -Uri "$BaseUrl$Path" -TimeoutSec 5
}

function Wait-Api {
    param([int]$Seconds = 15)
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

$exe = Join-Path $Root "desktop_v2\bin\TriCellPilot.exe"
Assert-True (Test-Path $exe) "Executable introuvable: $exe"

$startedProcess = $null
try {
    try {
        $state = Invoke-Json "/api/state"
    } catch {
        $startedProcess = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
        $state = Wait-Api 20
    }

    Assert-True ($null -ne $state.Config) "/api/state: Config absent."
    Assert-True ($null -ne $state.Production) "/api/state: Production absent."
    Assert-True ($null -ne $state.Diagnostic) "/api/state: Diagnostic absent."

    if ($state.Production.QualityIntervals -and $state.Production.QualityIntervals.Count -gt 0) {
        Assert-True ($state.Production.QualityIntervals.Count -eq 9) "/api/state: QualityIntervals doit contenir 9 intervalles quand present."
    }

    $recipes = Invoke-Json "/api/recipes/intelligent"
    foreach ($cellType in @("21700", "18650")) {
        $recipe = $recipes.recipes.$cellType
        Assert-True ($null -ne $recipe) "/api/recipes/intelligent: recette $cellType absente."
        Assert-True ([int]$recipe.SampleSize -eq 19) "Recette ${cellType}: SampleSize doit valoir 19."

        foreach ($lane in 1..10) {
            Assert-True ($recipe.GoodLanes -contains ([string]$lane)) "Recette ${cellType}: ligne GOOD $lane absente."
        }

        Assert-True ([int]$recipe.SampleSize -eq 19) "Recette ${cellType}: l'apprentissage logiciel doit rester a 19 cellules."
    }

    $audit = Invoke-Json "/api/cells/audit?limit=5"
    Assert-True ($null -ne $audit.cells) "/api/cells/audit: cells absent."

    $csv = Invoke-WebRequest -Uri "$BaseUrl/api/export/cells-audit-csv" -UseBasicParsing -TimeoutSec 5
    $header = ($csv.Content -split "`r?`n") | Select-Object -First 1
    foreach ($column in @("routing_model", "quality_interval", "voltage_min", "voltage_max", "ir_min", "ir_max")) {
        Assert-True ($header -match $column) "CSV audit: colonne manquante $column."
    }

    $contracts = Invoke-Json "/api/contracts"
    Assert-True ($null -ne $contracts) "/api/contracts indisponible."

    $maintenance = Invoke-Json "/api/maintenance"
    Assert-True ($null -ne $maintenance.maintenance) "/api/maintenance indisponible."
    try {
        Invoke-WebRequest -Uri "$BaseUrl/api/maintenance/piston-test" -Method Post -ContentType "application/json" -Body '{"lane":"BOGUS"}' -UseBasicParsing -TimeoutSec 5 | Out-Null
        throw "/api/maintenance/piston-test: une ligne invalide doit etre refusee."
    } catch {
        $statusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        Assert-True ($statusCode -eq 400) "/api/maintenance/piston-test: attendu HTTP 400 sur ligne invalide, obtenu $statusCode."
    }

    Write-Host "ApiSmokeCheck OK"
}
finally {
    if ($startedProcess -and -not $startedProcess.HasExited) {
        Stop-Process -Id $startedProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
