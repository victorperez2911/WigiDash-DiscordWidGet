Add-Type -AssemblyName System.Drawing

$bitmap = New-Object System.Drawing.Bitmap(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

# Discord blurple background
$graphics.Clear([System.Drawing.Color]::FromArgb(88, 101, 242))

# Draw white 'D'
$font = New-Object System.Drawing.Font('Arial', 28, [System.Drawing.FontStyle]::Bold)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$graphics.DrawString('D', $font, $brush, 15, 8)

# Save
$bitmap.Save('icon.png', [System.Drawing.Imaging.ImageFormat]::Png)

# Cleanup
$graphics.Dispose()
$font.Dispose()
$brush.Dispose()
$bitmap.Dispose()

Write-Host "Icon created successfully: icon.png"
