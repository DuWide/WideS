# Renders UiIconFactory geometries into a single PNG sheet for visual review.
param(
    [string]$SourceFile = (Join-Path $PSScriptRoot '..\UiIconFactory.cs'),
    [string]$OutputFile = (Join-Path $PSScriptRoot '..\obj\icon-preview.png')
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$text = Get-Content -LiteralPath $SourceFile -Raw
$entries = [regex]::Matches($text, '\["(?<name>[^"]+)"\]\s*=\s*new\((?<body>[\s\S]*?)\)\s*,?\s*\r?\n')

$icons = foreach ($entry in $entries) {
    $body = $entry.Groups['body'].Value
    $data = ([regex]::Matches($body, '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value }) -join ''
    [pscustomobject]@{
        Name   = $entry.Groups['name'].Value
        Data   = $data
        Filled = $body -match 'Filled:\s*true'
    }
}

if ($icons.Count -eq 0) { throw 'No icons parsed.' }

$cell = 64
$columns = 8
$rows = [math]::Ceiling($icons.Count / $columns)
$width = $columns * $cell
$height = $rows * $cell

$visual = New-Object System.Windows.Media.DrawingVisual
$ctx = $visual.RenderOpen()
$background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(24, 26, 32))
$foreground = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(226, 232, 240))
$ctx.DrawRectangle($background, $null, (New-Object System.Windows.Rect 0, 0, $width, $height))

for ($i = 0; $i -lt $icons.Count; $i++) {
    $icon = $icons[$i]
    $geometry = [System.Windows.Media.Geometry]::Parse($icon.Data)

    $offsetX = ($i % $columns) * $cell + ($cell - 24) / 2
    $offsetY = [math]::Floor($i / $columns) * $cell + ($cell - 24) / 2

    $ctx.PushTransform((New-Object System.Windows.Media.TranslateTransform $offsetX, $offsetY))
    if ($icon.Filled) {
        $ctx.DrawGeometry($foreground, $null, $geometry)
    }
    else {
        $pen = New-Object System.Windows.Media.Pen $foreground, 1.7
        $pen.StartLineCap = 'Round'
        $pen.EndLineCap = 'Round'
        $pen.LineJoin = 'Round'
        $ctx.DrawGeometry($null, $pen, $geometry)
    }
    $ctx.Pop()
}

$ctx.Close()

$bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $width, $height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)

$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream = [System.IO.File]::Create($OutputFile)
$encoder.Save($stream)
$stream.Close()

Write-Output "ICONS=$($icons.Count)"
Write-Output "OUTPUT=$OutputFile"
