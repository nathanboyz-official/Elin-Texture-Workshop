# Builds the downloadable zip locally, the same way the GitHub workflow does.
# Useful for testing the download before tagging, or for uploading a release by hand.
#
#   pwsh scripts/package-release.ps1 -Version v1.0.0
param([string]$Version = "dev")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# The app locks its own executable while running.
Get-Process ElinTextureWorkshop -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Testing..."
dotnet test -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed; refusing to package." }

Write-Host "Publishing..."
dotnet publish src/ElinTextureManager.App -c Release -r win-x64 --self-contained true `
    -o publish/win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$name = "ElinTextureWorkshop-$Version-win-x64"
$zip = Join-Path $root "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }

Compress-Archive -Path (Join-Path $root 'publish/win-x64/*') -DestinationPath $zip

$mb = (Get-Item $zip).Length / 1MB
Write-Host ("Packaged {0} ({1:N1} MB)" -f $zip, $mb)
