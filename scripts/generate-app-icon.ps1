[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repositoryRoot 'src/VaultDelta.Desktop/Assets'
}
else {
    [System.IO.Path]::GetFullPath($OutputDirectory, $repositoryRoot)
}

Add-Type -AssemblyName System.Drawing.Common
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$canvasSize = 1024
$master = [System.Drawing.Bitmap]::new(
    $canvasSize,
    $canvasSize,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)

try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $tile = [System.Drawing.RectangleF]::new(64, 64, 896, 896)
    $radius = 224
    $tilePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $diameter = $radius * 2
        $tilePath.AddArc($tile.Left, $tile.Top, $diameter, $diameter, 180, 90)
        $tilePath.AddArc($tile.Right - $diameter, $tile.Top, $diameter, $diameter, 270, 90)
        $tilePath.AddArc($tile.Right - $diameter, $tile.Bottom - $diameter, $diameter, $diameter, 0, 90)
        $tilePath.AddArc($tile.Left, $tile.Bottom - $diameter, $diameter, $diameter, 90, 90)
        $tilePath.CloseFigure()

        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $tile,
            [System.Drawing.Color]::FromArgb(255, 38, 190, 160),
            [System.Drawing.Color]::FromArgb(255, 8, 124, 106),
            45.0)
        try {
            $graphics.FillPath($gradient, $tilePath)
        }
        finally {
            $gradient.Dispose()
        }

        $highlight = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 255, 255, 255), 10)
        try {
            $graphics.DrawPath($highlight, $tilePath)
        }
        finally {
            $highlight.Dispose()
        }
    }
    finally {
        $tilePath.Dispose()
    }

    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        [System.Drawing.PointF[]]$rightArrow = @(
            [System.Drawing.PointF]::new(220, 280),
            [System.Drawing.PointF]::new(560, 280),
            [System.Drawing.PointF]::new(560, 200),
            [System.Drawing.PointF]::new(800, 360),
            [System.Drawing.PointF]::new(560, 520),
            [System.Drawing.PointF]::new(560, 440),
            [System.Drawing.PointF]::new(220, 440)
        )
        [System.Drawing.PointF[]]$leftArrow = @(
            [System.Drawing.PointF]::new(804, 584),
            [System.Drawing.PointF]::new(464, 584),
            [System.Drawing.PointF]::new(464, 504),
            [System.Drawing.PointF]::new(224, 664),
            [System.Drawing.PointF]::new(464, 824),
            [System.Drawing.PointF]::new(464, 744),
            [System.Drawing.PointF]::new(804, 744)
        )
        $graphics.FillPolygon($white, $rightArrow)
        $graphics.FillPolygon($white, $leftArrow)
    }
    finally {
        $white.Dispose()
    }

    $pngPath = Join-Path $resolvedOutputDirectory 'VaultDelta.AppIcon.png'
    $master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $iconSizes = @(16, 24, 32, 48, 64, 128, 256)
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $iconSizes) {
        $frame = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $frameGraphics = [System.Drawing.Graphics]::FromImage($frame)
        try {
            $frameGraphics.Clear([System.Drawing.Color]::Transparent)
            $frameGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $frameGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $frameGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $frameGraphics.DrawImage($master, 0, 0, $size, $size)
            $stream = [System.IO.MemoryStream]::new()
            try {
                $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $frameGraphics.Dispose()
            $frame.Dispose()
        }
    }

    $icoPath = Join-Path $resolvedOutputDirectory 'VaultDelta.ico'
    $fileStream = [System.IO.File]::Create($icoPath)
    $writer = [System.IO.BinaryWriter]::new($fileStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)
        for ($index = 0; $index -lt $frames.Count; $index++) {
            $size = $iconSizes[$index]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frameBytes in $frames) {
            $writer.Write($frameBytes)
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }

    Write-Host "Generated $pngPath"
    Write-Host "Generated $icoPath"
}
finally {
    $graphics.Dispose()
    $master.Dispose()
}
