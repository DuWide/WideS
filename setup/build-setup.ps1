param(
    [switch]$SkipInno
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appDir = Join-Path $PSScriptRoot "WideS-Setup\app"
$outputDir = Join-Path $PSScriptRoot "output"

Set-Location $root

Get-Process -Name "WideS" -ErrorAction SilentlyContinue | Stop-Process -Force

$stagingParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "WideS-Setup")).TrimEnd('\')
$stagingApp = [IO.Path]::GetFullPath($appDir).TrimEnd('\')
if ([IO.Path]::GetDirectoryName($stagingApp) -ne $stagingParent -or
    [IO.Path]::GetFileName($stagingApp) -ne "app") {
    throw "Unsafe setup staging path: $stagingApp"
}
if (Test-Path -LiteralPath $stagingApp) {
    Remove-Item -LiteralPath $stagingApp -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingApp | Out-Null

Write-Host "Publishing self-contained WideS for win-x64..."
dotnet publish -c Release -p:PublishProfile=Setup-win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path (Join-Path $appDir "WideS.exe"))) {
    throw "WideS.exe not found in $appDir"
}

$dataDir = Join-Path $appDir "data"
if (Test-Path $dataDir) {
    Remove-Item $dataDir -Recurse -Force
    Write-Host "Removed bundled data folder (user data is created on first run)."
}

$sizeMb = [math]::Round((Get-ChildItem $appDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "App staged: $appDir ($sizeMb MB, .NET runtime included)"

if ($SkipInno) {
    Write-Host "SkipInno specified - installer was not built."
    exit 0
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $uninstallKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
    if (Test-Path $uninstallKey) {
        $installLocation = (Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).InstallLocation
        if ($installLocation) {
            $candidate = Join-Path $installLocation "ISCC.exe"
            if (Test-Path $candidate) { $iscc = $candidate }
        }
    }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$portableZip = Join-Path $outputDir "WideS-Setup-portable.zip"
if (Test-Path $portableZip) {
    Remove-Item $portableZip -Force
    Write-Host "Removed obsolete portable zip."
}

if (-not $iscc) {
    throw "Inno Setup 6 not found. Install: https://jrsoftware.org/isdl.php"
}

Write-Host "Building installer with Inno Setup..."
& $iscc (Join-Path $PSScriptRoot "WideS.iss")
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setupExe = Join-Path $outputDir "WideS-Setup.exe"
if (-not (Test-Path $setupExe)) {
    throw "Installer was not created: $setupExe"
}

$setupSizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host "Done: $setupExe ($setupSizeMb MB)"
Write-Host "Run this setup on the work PC over the existing install. Data in AppData\WideS is kept."
