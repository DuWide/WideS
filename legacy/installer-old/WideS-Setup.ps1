param(
    [switch]$RunAfterInstall
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $scriptRoot "app"
$target = Join-Path $env:LOCALAPPDATA "Programs\WideS"
$exe = Join-Path $target "DevCockpit.exe"

if (-not (Test-Path $source)) {
    throw "Install source not found: $source"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "WideS.lnk"
$shortcut = $shell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = $exe
$shortcut.Save()

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\WideS"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$startMenuShortcut = Join-Path $startMenuDir "WideS.lnk"
$shortcut = $shell.CreateShortcut($startMenuShortcut)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = $exe
$shortcut.Save()

Write-Host "WideS installed to: $target"
Write-Host "User data folder: $env:APPDATA\DevCockpit"

if ($RunAfterInstall) {
    Start-Process $exe
}
