# Create WigiDash Discord Widget Logo
# Combines Discord and WigiDash branding

$outputPath = "G:\Sources\WigiDash-DiscordWidGet\discord_app_icon.png"

# Check if custom logo already exists
if (Test-Path $outputPath) {
    Write-Host "Custom logo already exists at: $outputPath" -ForegroundColor Yellow
    Write-Host "Delete it first if you want to regenerate the default logo." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Opening existing logo..." -ForegroundColor Cyan
    Start-Process $outputPath
    exit 0
}

Add-Type -AssemblyName System.Drawing

# Load WigiDash logo if exists
$wigiDashLogoPath = "G:\Sources\WigiDash-DiscordWidGet\wigidash_logo.png"
$hasWigiDashLogo = Test-Path $wigiDashLogoPath

# Create 512x512 logo (Discord recommended size)
$size = 512
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

# Discord blurple background (rounded square)
$discordColor = [System.Drawing.Color]::FromArgb(88, 101, 242)
$brush = New-Object System.Drawing.SolidBrush($discordColor)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath

# Rounded rectangle
$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$radius = 80

$path.AddArc($rect.Left, $rect.Top, $radius, $radius, 180, 90)
$path.AddArc($rect.Right - $radius, $rect.Top, $radius, $radius, 270, 90)
$path.AddArc($rect.Right - $radius, $rect.Bottom - $radius, $radius, $radius, 0, 90)
$path.AddArc($rect.Left, $rect.Bottom - $radius, $radius, $radius, 90, 90)
$path.CloseFigure()

$graphics.FillPath($brush, $path)

# Draw Discord logo (simplified controller/gamepad shape)
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 8)

# Discord "clyde" logo simplified
$logoPath = New-Object System.Drawing.Drawing2D.GraphicsPath

# Head (rounded rectangle at top)
$headRect = New-Object System.Drawing.Rectangle(156, 120, 200, 180)
$logoPath.AddEllipse($headRect)

# Draw on graphics
$graphics.FillPath($whiteBrush, $logoPath)

# Eyes (two circles)
$eyeSize = 40
$eyeY = 200
$graphics.FillEllipse($brush, 200, $eyeY, $eyeSize, $eyeSize)  # Left eye
$graphics.FillEllipse($brush, 272, $eyeY, $eyeSize, $eyeSize)  # Right eye

# Draw WigiDash text at bottom
$font = New-Object System.Drawing.Font("Arial", 48, [System.Drawing.FontStyle]::Bold)
$text = "WIGIDASH"
$textSize = $graphics.MeasureString($text, $font)
$textX = ($size - $textSize.Width) / 2
$textY = $size - 120

$graphics.DrawString($text, $font, $whiteBrush, $textX, $textY)

# Save as PNG
$outputPath = "G:\Sources\WigiDash-DiscordWidGet\discord_app_icon.png"
$bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "Logo created successfully: $outputPath" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Open Discord Developer Portal" -ForegroundColor White
Write-Host "2. Go to your application (WigiDashRemoteControl)" -ForegroundColor White
Write-Host "3. Upload this image as App Icon" -ForegroundColor White
Write-Host ""
Write-Host "Image path: $outputPath" -ForegroundColor Cyan

# Cleanup
$graphics.Dispose()
$font.Dispose()
$brush.Dispose()
$whiteBrush.Dispose()
$pen.Dispose()
$path.Dispose()
$logoPath.Dispose()
$bitmap.Dispose()

# Open the image
Start-Process $outputPath
