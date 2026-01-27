# WigiDash Discord Widget - Deploy Script
# Automatiza build e deploy do Discord widget para o WigiDash

param(
    [string]$Configuration = "Release",
    [switch]$RestartWigiDash = $false
)

# Paths
$projectPath = "$PSScriptRoot"
$widgetGuid = "3035795C-15EC-4B09-859D-27D1178C949C"
$widgetDllName = "$widgetGuid.dll"
$wigiDashWidgetsPath = "$env:APPDATA\G.SKILL\WigiDashManager\Widgets"

Write-Host "=== WigiDash Discord Widget Deploy ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Step 1: Build
Write-Host "[1/4] Building project..." -ForegroundColor Green
dotnet build "$projectPath\DiscordWidget.csproj" -c $Configuration --source https://api.nuget.org/v3/index.json

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Step 1.5: Stop WigiDash (Critical for file access)
Write-Host "[1.5/4] Checking for running WigiDash..." -ForegroundColor Green
$wigiDashProcess = Get-Process -Name "WigiDashManager" -ErrorAction SilentlyContinue

if ($wigiDashProcess) {
    if ($RestartWigiDash) {
        Write-Host "Stopping WigiDash to release file locks..." -ForegroundColor Yellow
        Stop-Process -Name "WigiDashManager" -Force
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "WARNING: WigiDash is running. File copy might fail." -ForegroundColor Red
        Write-Host "Use -RestartWigiDash to auto-close it, or close manually." -ForegroundColor Yellow
    }
}

# Step 2: Prepare widget folder
$targetFolder = Join-Path $wigiDashWidgetsPath $widgetGuid

if (-not (Test-Path $wigiDashWidgetsPath)) {
    Write-Host "WigiDash Widgets folder not found: $wigiDashWidgetsPath" -ForegroundColor Red
    Write-Host "Make sure WigiDash is installed." -ForegroundColor Yellow
    exit 1
}

Write-Host "[2/4] Preparing widget folder..." -ForegroundColor Green
if (Test-Path $targetFolder) {
    try {
        Remove-Item $targetFolder -Recurse -Force -ErrorAction Stop
    }
    catch {
        Write-Host "Failed to clean folder. WigiDash might still be locking files." -ForegroundColor Red
        if (-not $RestartWigiDash) {
            Write-Host "Try running with -RestartWigiDash" -ForegroundColor Cyan
        }
        exit 1
    }
}
New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null

# Create Resources/Icons folder
$resourcesFolder = Join-Path $targetFolder "Resources\Icons"
New-Item -ItemType Directory -Path $resourcesFolder -Force | Out-Null

# Step 3: Copy files
Write-Host "[3/4] Copying widget files..." -ForegroundColor Green
$sourcePath = "$projectPath\bin\$Configuration\net472"

if (-not (Test-Path $sourcePath)) {
    Write-Host "Build output not found: $sourcePath" -ForegroundColor Red
    exit 1
}

# Copy main DLL
Copy-Item "$sourcePath\$widgetDllName" $targetFolder -Force
Write-Host "  [OK] $widgetDllName" -ForegroundColor Gray

# Copy dependencies (excluding main DLL)
$dependencies = @(
    "Discord.Net.Core.dll",
    "Discord.Net.Rest.dll",
    "Discord.Net.WebSocket.dll",
    "Discord.Net.Commands.dll",
    "Discord.Net.Webhook.dll",
    "Newtonsoft.Json.dll",
    "System.Interactive.Async.dll",
    "websocket-sharp.dll",  # WebSocket library for RPC (fixes Origin header issue)
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.Collections.Immutable.dll",
    "WigiDashWidgetFramework.dll"
)

foreach ($dep in $dependencies) {
    $depPath = Join-Path $sourcePath $dep
    if (Test-Path $depPath) {
        Copy-Item $depPath $targetFolder -Force
        Write-Host "  [OK] $dep" -ForegroundColor Gray
    }
    else {
        Write-Host "  [WARN] $dep (not found)" -ForegroundColor Yellow
    }
}

# Copy icon
if (Test-Path "$sourcePath\icon.png") {
    Copy-Item "$sourcePath\icon.png" $targetFolder -Force
    Write-Host "  [OK] icon.png" -ForegroundColor Gray
}
elseif (Test-Path "$projectPath\icon.png") {
    Copy-Item "$projectPath\icon.png" $targetFolder -Force
    Write-Host "  [OK] icon.png (from source)" -ForegroundColor Gray
}
else {
    Write-Host "  [WARN] icon.png not found" -ForegroundColor Yellow
}

# Copy Resources/Icons if they exist
if (Test-Path "$projectPath\Resources\Icons") {
    $iconFiles = Get-ChildItem "$projectPath\Resources\Icons\*.png" -ErrorAction SilentlyContinue
    foreach ($icon in $iconFiles) {
        Copy-Item $icon.FullName $resourcesFolder -Force
        Write-Host "  [OK] Resources/Icons/$($icon.Name)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Widget deployed to: $targetFolder" -ForegroundColor Cyan

# Step 4: Restart WigiDash (optional)
if ($RestartWigiDash) {
    Write-Host "[4/4] Restarting WigiDash..." -ForegroundColor Green

    # Try to start WigiDash
    $wigiDashExe = "C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashManager.exe"
    if (Test-Path $wigiDashExe) {
        Write-Host "Starting WigiDash..." -ForegroundColor Yellow
        Start-Process $wigiDashExe
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "WigiDashManager.exe not found at: $wigiDashExe" -ForegroundColor Yellow
        Write-Host "Please restart WigiDash manually." -ForegroundColor Yellow
    }
}
else {
    Write-Host "[4/4] Skipped WigiDash restart" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To load the widget, restart WigiDash manually or run:" -ForegroundColor Cyan
    Write-Host "  .\deploy.ps1 -RestartWigiDash" -ForegroundColor White
}

Write-Host ""
Write-Host "=== Deploy Complete! ===" -ForegroundColor Green
Write-Host ""
Write-Host "Widget Path: $targetFolder" -ForegroundColor Cyan
Write-Host "Widget GUID: $widgetGuid" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open WigiDash Manager" -ForegroundColor White
Write-Host "  2. Discord Widget should appear in the widget list" -ForegroundColor White
Write-Host "  3. Configure Discord Client ID in DiscordConfig.cs before using" -ForegroundColor White
Write-Host ""
