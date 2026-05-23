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
$gitignore = Join-Path $rootPath ".gitignore"

Assert-True (Test-Path $gitignore) ".gitignore manquant."

$gitignoreText = [System.IO.File]::ReadAllText($gitignore, [System.Text.Encoding]::UTF8)
foreach ($required in @(
    "desktop_v2/bin/",
    "desktop_app/",
    "backend/",
    "frontend/",
    "webview2_pkg/",
    "webview2.zip",
    "webview2.nupkg",
    "ODOO.txt",
    "odoo_config.json",
    ".env.*",
    "*.corrupt_*"
)) {
    Assert-True ($gitignoreText -match [regex]::Escape($required)) ".gitignore ne protege pas: $required"
}

$secretNamePatterns = @(
    "^\.env$",
    "^\.env\.",
    "^ODOO\.txt$",
    "^odoo_config\.json$",
    "secret",
    "token",
    "password"
)

$forbiddenFiles = Get-ChildItem -Path $rootPath -Recurse -Force -File -ErrorAction SilentlyContinue |
    Where-Object {
        $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\')
        if ($_.Name -eq ".env.example") {
            return $false
        }

        if ($relative -match '^(desktop_v2\\bin|desktop_app|backend|frontend|webview2_pkg)\\') {
            return $false
        }

        foreach ($pattern in $secretNamePatterns) {
            if ($_.Name -match $pattern) {
                return $true
            }
        }

        return $false
    }

Assert-True (($forbiddenFiles | Measure-Object).Count -eq 0) ("Fichier sensible detecte dans le depot: " + (($forbiddenFiles | Select-Object -ExpandProperty FullName) -join ", "))

$textExtensions = @(".cs", ".js", ".json", ".md", ".ps1", ".bat", ".html", ".css", ".txt", ".yml", ".yaml")
$suspiciousContent = Get-ChildItem -Path $rootPath -Recurse -Force -File -ErrorAction SilentlyContinue |
    Where-Object {
        $relative = $_.FullName.Substring($rootPath.Length).TrimStart('\')
        $textExtensions -contains $_.Extension.ToLowerInvariant() -and
        $relative -notmatch '^(desktop_v2\\bin|desktop_app|backend|frontend|webview2_pkg)\\' -and
        $_.Name -ne "RepositoryPreflight.ps1" -and
        $_.Name -ne ".env.example"
    } |
    ForEach-Object {
        $content = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
        if ($content -match '(?i)(api[_-]?key|bearer|password|secret|token)["''\s]*[:=]\s*["''][A-Za-z0-9_\-./+=]{16,}["'']') {
            $_.FullName
        }
    }

Assert-True (($suspiciousContent | Measure-Object).Count -eq 0) ("Contenu ressemblant a un secret detecte: " + ($suspiciousContent -join ", "))

Write-Host "RepositoryPreflight OK"
