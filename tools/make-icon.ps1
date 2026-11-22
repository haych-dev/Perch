# Renders the Perch app icon (PNG-in-ICO, 256px) with System.Drawing.
param([string]$Out = "$PSScriptRoot\..\Assets\perch.ico")

Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

function RoundRect($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($x, $y, $r, $r, 180, 90)
  $p.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
  $p.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90)
  $p.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
  $p.CloseFigure()
  return $p
}

# Tile: deep blue-violet gradient
$rect = New-Object System.Drawing.Rectangle 8, 8, 240, 240
$tile = RoundRect 8 8 240 240 64
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 58, 70, 140)), ([System.Drawing.Color]::FromArgb(255, 18, 20, 38)), 60
$g.FillPath($bg, $tile)

# Three tiles resting on a bar: the dock
$colors = @(
  [System.Drawing.Color]::FromArgb(255, 255, 180, 84),
  [System.Drawing.Color]::FromArgb(255, 79, 182, 255),
  [System.Drawing.Color]::FromArgb(255, 123, 224, 138))
for ($i = 0; $i -lt 3; $i++) {
  $x = 52 + $i * 54
  $h = 46; if ($i -eq 1) { $h = 62 }
  $y = 168 - $h
  $p = RoundRect $x $y 44 $h 14
  $b = New-Object System.Drawing.SolidBrush $colors[$i]
  $g.FillPath($b, $p)
}
$bar = RoundRect 36 172 184 30 15
$barBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
$g.FillPath($barBrush, $bar)
$g.Dispose()

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
$bw.Write($png)
$bw.Close()
"Wrote $Out ($($png.Length) bytes)"
