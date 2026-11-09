$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '../public/icons'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
# Reuse the equalizer mark from docs/logo.svg; keep it inside the maskable safe circle.
foreach ($size in @(180, 192, 512)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(6, 10, 9))
    $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(43, 227, 124))
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(6, 10, 9))
    $scale = [single]($size * 0.8 / 128)
    $graphics.TranslateTransform([single]($size * 0.1), [single]($size * 0.1))
    $graphics.ScaleTransform($scale, $scale)
    $graphics.FillEllipse($green, 2, 2, 124, 124)
    foreach ($bar in @(@(28,46,36), @(44,33,62), @(60,22,84), @(76,37,54), @(92,49,30))) {
        $graphics.FillRectangle($dark, $bar[0], ($bar[1]+5), 10, ($bar[2]-10))
        $graphics.FillEllipse($dark, $bar[0], $bar[1], 10, 10)
        $graphics.FillEllipse($dark, $bar[0], ($bar[1]+$bar[2]-10), 10, 10)
    }
    $name = if ($size -eq 180) { 'apple-touch-icon.png' } else { "icon-$size.png" }
    $bitmap.Save((Join-Path $destination $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose(); $green.Dispose(); $dark.Dispose()
}
