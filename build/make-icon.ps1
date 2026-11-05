$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $padding = [float]($size * 0.035)
        $rect = New-Object System.Drawing.RectangleF(
            $padding,
            $padding,
            [float]($size - (2 * $padding)),
            [float]($size - (2 * $padding))
        )
        $radius = [float]($size * 0.22)
        $diameter = $radius * 2

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($rect.Left, $rect.Top, $diameter, $diameter, 180, 90)
        $path.AddArc($rect.Right - $diameter, $rect.Top, $diameter, $diameter, 270, 90)
        $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc($rect.Left, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()

        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 20, 183, 159),
            [System.Drawing.Color]::FromArgb(255, 7, 112, 102),
            132.0
        )
        $graphics.FillPath($gradient, $path)

        $innerRect = New-Object System.Drawing.RectangleF(
            [float]($rect.Left + $size * 0.012),
            [float]($rect.Top + $size * 0.012),
            [float]($rect.Width - $size * 0.024),
            [float]($rect.Height - $size * 0.024)
        )
        $innerRadius = [float]($size * 0.205)
        $innerDiameter = $innerRadius * 2
        $innerPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $innerPath.AddArc($innerRect.Left, $innerRect.Top, $innerDiameter, $innerDiameter, 180, 90)
        $innerPath.AddArc($innerRect.Right - $innerDiameter, $innerRect.Top, $innerDiameter, $innerDiameter, 270, 90)
        $innerPath.AddArc($innerRect.Right - $innerDiameter, $innerRect.Bottom - $innerDiameter, $innerDiameter, $innerDiameter, 0, 90)
        $innerPath.AddArc($innerRect.Left, $innerRect.Bottom - $innerDiameter, $innerDiameter, $innerDiameter, 90, 90)
        $innerPath.CloseFigure()
        $innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(42, 255, 255, 255), [float]([Math]::Max(1.0, $size * 0.008)))
        $graphics.DrawPath($innerPen, $innerPath)

        # A clean geometric M, drawn as a stroke so it stays legible at small sizes.
        $strokeWidth = [float]($size * 0.102)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $strokeWidth)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        $leftX = [float]($size * 0.292)
        $rightX = [float]($size * 0.708)
        $topY = [float]($size * 0.355)
        $bottomY = [float]($size * 0.676)
        $midX = [float]($size * 0.5)
        $midY = [float]($size * 0.575)

        $points = @(
            (New-Object System.Drawing.PointF($leftX, $bottomY)),
            (New-Object System.Drawing.PointF($leftX, $topY)),
            (New-Object System.Drawing.PointF($midX, $midY)),
            (New-Object System.Drawing.PointF($rightX, $topY)),
            (New-Object System.Drawing.PointF($rightX, $bottomY))
        )
        $graphics.DrawLines($pen, [System.Drawing.PointF[]]$points)

        $pen.Dispose()
        $innerPen.Dispose()
        $innerPath.Dispose()
        $gradient.Dispose()
        $path.Dispose()
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$memory.ToArray()
    if ($size -eq 256) {
        $bitmap.Save((Join-Path $assets 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $memory.Dispose()
    $bitmap.Dispose()
}

$iconPath = Join-Path $assets 'app.ico'
$file = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $data = $images[$index]
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$data.Length)
        $writer.Write([UInt32]$offset)
        $offset += $data.Length
    }

    foreach ($data in $images) {
        $writer.Write($data)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $iconPath
Write-Output (Join-Path $assets 'icon-preview.png')
