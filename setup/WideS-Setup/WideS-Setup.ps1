param(
    [switch]$RunAfterInstall
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $scriptRoot "app"
$target = Join-Path $env:LOCALAPPDATA "Programs\WideS"
$exe = Join-Path $target "WideS.exe"

if (-not (Test-Path $source)) {
    throw "Папка установки не найдена: $source`nСначала запустите setup\build-setup.bat на машине разработчика."
}

if (-not (Test-Path (Join-Path $source "WideS.exe"))) {
    throw "WideS.exe не найден в $source`nСначала соберите setup через setup\build-setup.bat"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell

$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "WideS.lnk"
$shortcut = $shell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\WideS"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$startMenuShortcut = Join-Path $startMenuDir "WideS.lnk"
$shortcut = $shell.CreateShortcut($startMenuShortcut)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()

Write-Host ""
Write-Host "WideS установлен: $target"
Write-Host "Данные пользователя: $env:APPDATA\WideS"
Write-Host "При первом запуске программа попросит имя и пароль."
Write-Host ""

if ($RunAfterInstall) {
    Start-Process $exe
}
