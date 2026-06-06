$ErrorActionPreference = 'Stop'
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path (Split-Path -Parent $ProjectDir) 'outputs'
$ExeName = 'PhonePhotoReturn'

Set-Location $ProjectDir
python -m PyInstaller `
  --noconfirm `
  --clean `
  --onefile `
  --windowed `
  --name $ExeName `
  --icon "assets\icon.ico" `
  --add-data "assets;assets" `
  --add-data "templates;templates" `
  app.py

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -LiteralPath (Join-Path $ProjectDir "dist\$ExeName.exe") -Destination $OutputDir -Force
$SourceZipItems = @(
  (Join-Path $ProjectDir 'app.py'),
  (Join-Path $ProjectDir 'assets'),
  (Join-Path $ProjectDir 'templates'),
  (Join-Path $ProjectDir 'requirements.txt'),
  (Join-Path $ProjectDir 'build.ps1'),
  (Join-Path $ProjectDir 'README.md')
)
Compress-Archive -Path $SourceZipItems -DestinationPath (Join-Path $OutputDir 'PhonePhotoReturn-source.zip') -Force

Write-Host "Built EXE: $OutputDir\$ExeName.exe"
Write-Host "Source ZIP: $OutputDir\PhonePhotoReturn-source.zip"
