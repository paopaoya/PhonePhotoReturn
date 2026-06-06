$ErrorActionPreference = 'Stop'
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path (Split-Path -Parent $ProjectDir) 'outputs'

Set-Location $ProjectDir
gradle assembleDebug

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -LiteralPath (Join-Path $ProjectDir 'app\build\outputs\apk\debug\app-debug.apk') -Destination (Join-Path $OutputDir 'PhonePhotoReturn-Android-debug.apk') -Force

$SourceItems = @(
  (Join-Path $ProjectDir 'settings.gradle'),
  (Join-Path $ProjectDir 'build.gradle'),
  (Join-Path $ProjectDir 'gradle.properties'),
  (Join-Path $ProjectDir 'README.md'),
  (Join-Path $ProjectDir 'build.ps1'),
  (Join-Path $ProjectDir 'app\build.gradle'),
  (Join-Path $ProjectDir 'app\libs'),
  (Join-Path $ProjectDir 'app\src')
)
Compress-Archive -Path $SourceItems -DestinationPath (Join-Path $OutputDir 'PhonePhotoReturn-Android-source.zip') -Force

Write-Host "Built APK: $OutputDir\PhonePhotoReturn-Android-debug.apk"
Write-Host "Source ZIP: $OutputDir\PhonePhotoReturn-Android-source.zip"
