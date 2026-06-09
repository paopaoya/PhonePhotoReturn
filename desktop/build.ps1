$ErrorActionPreference = 'Stop'

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoDir = Split-Path -Parent $ProjectDir
$OutputDir = Join-Path $RepoDir 'outputs'
$NetProjectDir = Join-Path $ProjectDir 'net452'
$ProjectFile = Join-Path $NetProjectDir 'PhonePhotoReturn.Net452.csproj'
$ExeName = 'PhonePhotoReturn'

function Find-MSBuild {
  $candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe'),
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
  )

  foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
      return $candidate
    }
  }

  $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
  if ($command) {
    return $command.Source
  }

  throw 'MSBuild.exe was not found. Install Visual Studio Build Tools or .NET Framework build tools.'
}

function Get-NuGet {
  $command = Get-Command nuget.exe -ErrorAction SilentlyContinue
  if ($command) {
    return $command.Source
  }

  $toolsDir = Join-Path $ProjectDir '.tools'
  $nuget = Join-Path $toolsDir 'nuget.exe'
  if (!(Test-Path -LiteralPath $nuget)) {
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget
  }
  return $nuget
}

Set-Location $NetProjectDir

$nuget = Get-NuGet
& $nuget install (Join-Path $NetProjectDir 'packages.config') -OutputDirectory (Join-Path $NetProjectDir 'packages') -NonInteractive

$msbuild = Find-MSBuild
& $msbuild $ProjectFile /p:Configuration=Release /p:Platform=AnyCPU /t:Rebuild /verbosity:minimal

$builtExe = Join-Path $NetProjectDir "bin\Release\$ExeName.exe"
if (!(Test-Path -LiteralPath $builtExe)) {
  throw "Build did not produce $builtExe"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -LiteralPath $builtExe -Destination (Join-Path $OutputDir "$ExeName.exe") -Force

$sourceZipItems = @(
  (Join-Path $ProjectDir 'app.py'),
  (Join-Path $ProjectDir 'requirements.txt'),
  (Join-Path $ProjectDir 'templates'),
  (Join-Path $ProjectDir 'assets'),
  (Join-Path $ProjectDir 'build.ps1'),
  (Join-Path $ProjectDir 'build-python.ps1'),
  (Join-Path $ProjectDir 'README.md'),
  (Join-Path $NetProjectDir 'PhonePhotoReturn.Net452.csproj'),
  (Join-Path $NetProjectDir 'packages.config'),
  (Join-Path $NetProjectDir 'AssemblyLoader.cs'),
  (Join-Path $NetProjectDir 'MainForm.cs'),
  (Join-Path $NetProjectDir 'MobilePage.cs'),
  (Join-Path $NetProjectDir 'MultipartFormData.cs'),
  (Join-Path $NetProjectDir 'PhotoServer.cs'),
  (Join-Path $NetProjectDir 'Program.cs'),
  (Join-Path $NetProjectDir 'Properties')
)

Compress-Archive -Path $sourceZipItems -DestinationPath (Join-Path $OutputDir 'PhonePhotoReturn-source.zip') -Force

Write-Host "Built .NET 4.5.2 EXE: $OutputDir\$ExeName.exe"
Write-Host "Source ZIP: $OutputDir\PhonePhotoReturn-source.zip"
