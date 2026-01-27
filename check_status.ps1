# Check Discord Widget Status and Logs

Write-Host "=== Discord Widget Status Check ===" -ForegroundColor Cyan
Write-Host ""

# Check if global auth file exists
$globalAuthPath = Join-Path $env:APPDATA "G.SKILL\WigiDashManager\WidgetData\DiscordWidget\global_auth.dat"
Write-Host "Global Auth File: " -NoNewline
if (Test-Path $globalAuthPath) {
    $authFile = Get-Item $globalAuthPath
    Write-Host "EXISTS" -ForegroundColor Green
    Write-Host "  Last Modified: $($authFile.LastWriteTime)" -ForegroundColor Gray
    Write-Host "  Size: $($authFile.Length) bytes" -ForegroundColor Gray
} else {
    Write-Host "NOT FOUND" -ForegroundColor Red
    Write-Host "  (Token will be saved here after first login)" -ForegroundColor Yellow
}

Write-Host ""

# Check widget installation
$widgetPath = Join-Path $env:APPDATA "G.SKILL\WigiDashManager\Widgets\3035795C-15EC-4B09-859D-27D1178C949C"
Write-Host "Widget Installation: " -NoNewline
if (Test-Path $widgetPath) {
    Write-Host "INSTALLED" -ForegroundColor Green
    $dll = Join-Path $widgetPath "3035795C-15EC-4B09-859D-27D1178C949C.dll"
    if (Test-Path $dll) {
        $dllFile = Get-Item $dll
        Write-Host "  DLL Last Modified: $($dllFile.LastWriteTime)" -ForegroundColor Gray
    }
} else {
    Write-Host "NOT FOUND" -ForegroundColor Red
}

Write-Host ""

# Check if Discord is running
Write-Host "Discord Client: " -NoNewline
$discordProcess = Get-Process -Name "Discord" -ErrorAction SilentlyContinue
if ($discordProcess) {
    Write-Host "RUNNING" -ForegroundColor Green
    Write-Host "  Process ID: $($discordProcess.Id)" -ForegroundColor Gray
} else {
    Write-Host "NOT RUNNING" -ForegroundColor Red
    Write-Host "  (RPC requires Discord to be running)" -ForegroundColor Yellow
}

Write-Host ""

# Check if WigiDash is running
Write-Host "WigiDash Manager: " -NoNewline
$wigiDashProcess = Get-Process -Name "WigiDashManager" -ErrorAction SilentlyContinue
if ($wigiDashProcess) {
    Write-Host "RUNNING" -ForegroundColor Green
    Write-Host "  Process ID: $($wigiDashProcess.Id)" -ForegroundColor Gray
} else {
    Write-Host "NOT RUNNING" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Cyan
Write-Host "1. Make sure Discord is running"
Write-Host "2. Open WigiDash Manager"
Write-Host "3. Add Discord Widget to a page"
Write-Host "4. Open widget settings and click 'Login with Discord'"
Write-Host "5. Check console output for 'RPC' messages"
Write-Host ""
