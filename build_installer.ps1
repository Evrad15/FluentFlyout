# Build and generate Inno Setup installer for FluentFlyout
[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  FluentFlyout Installer Builder (Inno Setup)     " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

$DotnetPath = "C:\Users\Evrad\AppData\Local\Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $DotnetPath)) {
    $DotnetPath = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
}

$IsccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $IsccPath)) {
    $IsccPath = "C:\Program Files\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $IsccPath)) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { $IsccPath = $cmd.Source }
}

if (-not (Test-Path $IsccPath)) {
    Write-Error "Inno Setup Compiler (ISCC.exe) not found! Please install Inno Setup 6."
    exit 1
}

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $RootDir "FluentFlyoutWPF\FluentFlyout.csproj"
$PublishDir = Join-Path $RootDir "FluentFlyoutWPF\bin\publish\win-x64-standalone"
$IssFile = Join-Path $RootDir "installer\FluentFlyout.iss"
$OutputDir = Join-Path $RootDir "installer_output"

Write-Host "`n[1/3] Publishing FluentFlyout ($Configuration, $Runtime, Self-Contained)..." -ForegroundColor Yellow

# Terminate running FluentFlyout if locking the binary
Stop-Process -Name "FluentFlyout" -Force -ErrorAction SilentlyContinue

& $DotnetPath publish $ProjectFile -c $Configuration -r $Runtime --self-contained true -o $PublishDir --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed!"
    exit $LASTEXITCODE
}

Write-Host "`n[2/3] Compiling Inno Setup package..." -ForegroundColor Yellow
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

& $IsccPath $IssFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed!"
    exit $LASTEXITCODE
}

Write-Host "`n[3/3] Installer build complete!" -ForegroundColor Green
$Installers = Get-ChildItem -Path $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending
foreach ($inst in $Installers) {
    $sizeMb = [math]::Round($inst.Length / 1MB, 2)
    Write-Host "  -> Output: $($inst.FullName) ($sizeMb MB)" -ForegroundColor Green
}
Write-Host "==================================================" -ForegroundColor Cyan
