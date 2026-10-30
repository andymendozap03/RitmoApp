$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
$images = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(43,227,124))
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(6,10,9))
    $graphics.FillEllipse($green, 0, 0, $size-1, $size-1)
    $heights = @(0.28, 0.48, 0.66, 0.42, 0.24)
    for ($i = 0; $i -lt 5; $i++) {
        $h = [single]($size * $heights[$i]); $w = [single]($size * 0.08)
        $x = [single]($size * (0.22 + 0.12 * $i)); $y = [single](($size - $h)/2)
        $graphics.FillRectangle($dark, $x, $y, $w, $h)
    }
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@($size, $stream.ToArray())
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $green.Dispose(); $dark.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetDir 'ritmo.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16*$images.Count
foreach ($entry in $images) {
    $dim = if ($entry[0] -eq 256) { 0 } else { $entry[0] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry[1].Length); $writer.Write([uint32]$offset)
    $offset += $entry[1].Length
}
foreach ($entry in $images) { $writer.Write([byte[]]$entry[1]) }
$writer.Dispose(); $file.Dispose()
