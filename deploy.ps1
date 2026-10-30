# OpenWigiDash Widget Development - Deploy Script (branch net10)
# Build e deploy do widget Discord para o OpenWigiDashManager.
# A main guarda a tentativa net472 (parada no erro 4001 do WebSocket).

param(
    [string]$Configuration = "Debug",
    [switch]$RestartManager = $false,
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = 'Stop'

$projectPath = "$PSScriptRoot"
$name = "DiscordWidget"
$widgetsPath = Join-Path $env:LOCALAPPDATA 'OpenWigiDash\Widgets'
$targetFolder = Join-Path $widgetsPath $name
$staging = Join-Path $projectPath "bin\publish\$Configuration"

Write-Host "=== Discord (OpenWigiDash) Deploy ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Passo 1: build (publish leva as dependências do plugin; SDK e MudBlazor ficam com o host)
Write-Host "[1/4] Building..." -ForegroundColor Green
if (-not $SkipBuild) {
    dotnet publish "$projectPath\DiscordWidget.csproj" -c $Configuration -o $staging
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $staging "$name.dll"))) {
    throw "Build output not found: $staging"
}

# Passo 2: o manager segura a DLL carregada; precisa estar fechado para trocar
Write-Host "[2/4] Checking for running OpenWigiDash Manager..." -ForegroundColor Green
$manager = Get-Process -Name 'OpenWigiDashManager' -ErrorAction SilentlyContinue
if ($manager) {
    if ($RestartManager) {
        $managerPath = $manager[0].Path
        Write-Host "Stopping OpenWigiDash Manager to release file locks..." -ForegroundColor Yellow
        $manager | Stop-Process -Force
        Start-Sleep -Seconds 3
    }
    else {
        Write-Host "OpenWigiDash Manager is running. Use -RestartManager to close it automatically." -ForegroundColor Red
        exit 1
    }
}

# Passo 3: backup e cópia
Write-Host "[3/4] Copying widget files..." -ForegroundColor Green
if (Test-Path $targetFolder) {
    $backupFolder = Join-Path $env:LOCALAPPDATA ("OpenWigiDash\Backups\$name\" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
    Copy-Item -Path (Join-Path $targetFolder '*') -Destination $backupFolder -Recurse -Force
    Write-Host "Previous widget backed up to: $backupFolder" -ForegroundColor Cyan
    Remove-Item $targetFolder -Recurse -Force
}
New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $targetFolder -Recurse -Force
Write-Host "Widget deployed to: $targetFolder" -ForegroundColor Cyan

# Passo 4: reabrir o manager
if ($RestartManager -and $managerPath) {
    Write-Host "[4/4] Restarting OpenWigiDash Manager..." -ForegroundColor Green
    Start-Process $managerPath -ArgumentList '--tray'
}
else {
    Write-Host "[4/4] Open the OpenWigiDash Manager to load the widget." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Deploy Complete! ===" -ForegroundColor Green
