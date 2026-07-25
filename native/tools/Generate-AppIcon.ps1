[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot '..\assets\OpsMonitor.ico'
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$bitmap = New-Object Drawing.Bitmap 256, 256
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.Clear([Drawing.Color]::Transparent)

function New-RoundedPath {
    param(
        [Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

try {
    $shellRectangle = New-Object Drawing.RectangleF 12, 12, 232, 232
    $shellPath = New-RoundedPath -Rectangle $shellRectangle -Radius 52
    $shellBrush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        $shellRectangle,
        [Drawing.Color]::FromArgb(255, 8, 13, 21),
        [Drawing.Color]::FromArgb(255, 14, 25, 36),
        45
    )
    $shellPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 48, 79, 96)), 4
    $graphics.FillPath($shellBrush, $shellPath)
    $graphics.DrawPath($shellPen, $shellPath)

    $moduleColors = @(
        [Drawing.Color]::FromArgb(255, 67, 231, 245),
        [Drawing.Color]::FromArgb(255, 240, 90, 214),
        [Drawing.Color]::FromArgb(255, 67, 231, 210),
        [Drawing.Color]::FromArgb(255, 98, 167, 255)
    )
    $moduleRectangles = @(
        (New-Object Drawing.RectangleF 42, 42, 76, 76),
        (New-Object Drawing.RectangleF 138, 42, 76, 76),
        (New-Object Drawing.RectangleF 42, 138, 76, 76),
        (New-Object Drawing.RectangleF 138, 138, 76, 76)
    )

    for ($index = 0; $index -lt $moduleRectangles.Count; $index++) {
        $modulePath = New-RoundedPath -Rectangle $moduleRectangles[$index] -Radius 18
        $moduleBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 19, 30, 42))
        $modulePen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(150, $moduleColors[$index])), 3
        $accentBrush = New-Object Drawing.SolidBrush $moduleColors[$index]
        try {
            $graphics.FillPath($moduleBrush, $modulePath)
            $graphics.DrawPath($modulePen, $modulePath)
            $accentRectangle = New-Object Drawing.RectangleF(
                ($moduleRectangles[$index].X + 14),
                ($moduleRectangles[$index].Bottom - 22),
                48,
                7
            )
            $accentPath = New-RoundedPath -Rectangle $accentRectangle -Radius 3.5
            $graphics.FillPath($accentBrush, $accentPath)
            $accentPath.Dispose()
        }
        finally {
            $modulePath.Dispose()
            $moduleBrush.Dispose()
            $modulePen.Dispose()
            $accentBrush.Dispose()
        }
    }

    $statusBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 99, 230, 166))
    $graphics.FillEllipse($statusBrush, 209, 25, 18, 18)

    $pngStream = New-Object IO.MemoryStream
    $bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    $fileStream = [IO.File]::Open(
        $resolvedOutput,
        [IO.FileMode]::Create,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None
    )
    $writer = New-Object IO.BinaryWriter $fileStream
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$pngBytes.Length)
        $writer.Write([uint32]22)
        $writer.Write($pngBytes)
    }
    finally {
        $writer.Dispose()
        $pngStream.Dispose()
        $statusBrush.Dispose()
        $shellPen.Dispose()
        $shellBrush.Dispose()
        $shellPath.Dispose()
    }
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Output $resolvedOutput
