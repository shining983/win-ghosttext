#Requires -Version 5.1
<#
.SYNOPSIS
  Self-contained win-x64 publish + zip for green distribution.
#>
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\WinGhostText.App\WinGhostText.App.csproj"
$OutDir = Join-Path $Root "artifacts\win-x64"
$ZipPath = Join-Path $Root "artifacts\WinGhostText-win-x64.zip"

Write-Host "Publishing WinGhostText (win-x64, self-contained)..."
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path (Split-Path $OutDir) -Force | Out-Null

dotnet publish $Project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o $OutDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $ZipPath -Force

Write-Host "OK: $ZipPath"
Get-Item $ZipPath | Format-List FullName, Length, LastWriteTime
