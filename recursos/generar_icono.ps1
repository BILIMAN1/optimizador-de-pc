# Genera recursos\icono.ico (16 a 256 px, PNG dentro de ICO) con el mismo logo que dibuja la app
Add-Type -AssemblyName System.Drawing
$tamanos = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($t in $tamanos) {
    $bmp = New-Object Drawing.Bitmap $t, $t
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $m = [math]::Max(1, [int]($t * 0.04))
    $r = New-Object Drawing.RectangleF $m, $m, ($t - 2 * $m), ($t - 2 * $m)
    $rad = $r.Width * 0.24; $d = $rad * 2
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90); $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90); $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90); $p.CloseFigure()
    $b = New-Object Drawing.Drawing2D.LinearGradientBrush $r, ([Drawing.Color]::FromArgb(94, 234, 212)), ([Drawing.Color]::FromArgb(13, 148, 136)), 45
    $g.FillPath($b, $p)
    $pts = @(@(0.58,0.10),@(0.24,0.56),@(0.47,0.56),@(0.41,0.90),@(0.76,0.43),@(0.53,0.43)) | ForEach-Object {
        New-Object Drawing.PointF ($r.X + $_[0] * $r.Width), ($r.Y + $_[1] * $r.Height)
    }
    $g.FillPolygon((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(6, 46, 42))), [Drawing.PointF[]]$pts)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    ,$ms.ToArray()
}
$salida = Join-Path $PSScriptRoot 'icono.ico'
$fs = [IO.File]::Create($salida); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $t = $tamanos[$i]; $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Close()
"Icono creado: $salida"
