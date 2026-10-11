$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'src\UptimeKite.Windows\Assets\app.ico'
New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force | Out-Null
$images = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $scale = $size / 144.0
    $graphics.ScaleTransform([single]$scale,[single]$scale)
    $graphics.TranslateTransform(8,0)
    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#82AEFF'))
    $mint = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#51DCC1'))
    $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(64,4),[System.Drawing.PointF]::new(120,56),[System.Drawing.PointF]::new(64,112),[System.Drawing.PointF]::new(8,56))
    $graphics.FillPolygon($blue,$points)
    $graphics.FillPolygon($mint,[System.Drawing.PointF[]]@($points[0],$points[2],$points[3]))
    $pen = [System.Drawing.Pen]::new($blue.Color,6)
    $graphics.DrawLines($pen,[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(64,112),[System.Drawing.PointF]::new(44,130),[System.Drawing.PointF]::new(74,140)))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose(); $pen.Dispose(); $blue.Dispose(); $mint.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create($output)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -eq 256) {0} else {$image.Size}
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
$writer.Dispose(); $file.Dispose()
Write-Output "Created $output"
