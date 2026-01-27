# View Discord Widget Logs

$logDir = Join-Path $env:APPDATA "G.SKILL\WigiDashManager\WidgetData\DiscordWidget\Logs"

Write-Host "=== Discord Widget Logs ===" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $logDir) {
    $logFiles = Get-ChildItem $logDir -Filter "discord_widget_*.log" | Sort-Object LastWriteTime -Descending

    if ($logFiles.Count -gt 0) {
        Write-Host "Found $($logFiles.Count) log file(s):" -ForegroundColor Green
        Write-Host ""

        foreach ($file in $logFiles) {
            Write-Host "  $($file.Name)" -ForegroundColor White
            Write-Host "    Last Modified: $($file.LastWriteTime)" -ForegroundColor Gray
            Write-Host "    Size: $($file.Length) bytes" -ForegroundColor Gray
            Write-Host ""
        }

        $latestLog = $logFiles[0]
        Write-Host "=== Latest Log Content ===" -ForegroundColor Yellow
        Write-Host ""
        Get-Content $latestLog.FullName | Select-Object -Last 50

        Write-Host ""
        Write-Host "Opening log directory..." -ForegroundColor Cyan
        Start-Process "explorer.exe" $logDir
    } else {
        Write-Host "No log files found." -ForegroundColor Yellow
        Write-Host "Logs will be created when you add a Discord Widget." -ForegroundColor Gray
    }
} else {
    Write-Host "Log directory does not exist yet." -ForegroundColor Yellow
    Write-Host "Directory: $logDir" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
