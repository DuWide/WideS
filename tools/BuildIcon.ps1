Add-Type -AssemblyName System.Drawing
$destDir = Join-Path $PSScriptRoot "..\Assets"
$src = Join-Path $destDir "WideS.png"
$icoPath = Join-Path $destDir "WideS.ico"
$bmp = [System.Drawing.Bitmap]::FromFile($src)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = New-Object System.Collections.Generic.List[byte[]]
foreach ($size in $sizes) {
    $scaled = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($bmp, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $scaled.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray()) | Out-Null
    $scaled.Dispose()
}
$bw = New-Object System.IO.BinaryWriter([System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create))
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = [byte]$sizes[$i]
    $bw.Write($size)
    $bw.Write($size)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
$bmp.Dispose()
Write-Host "Created $icoPath"
