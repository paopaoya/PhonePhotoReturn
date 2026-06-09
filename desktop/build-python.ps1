$ErrorActionPreference = 'Stop'
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path (Split-Path -Parent $ProjectDir) 'outputs'
$ExeName = 'PhonePhotoReturn-Python'

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

Write-Host "Built Python EXE: $OutputDir\$ExeName.exe"
