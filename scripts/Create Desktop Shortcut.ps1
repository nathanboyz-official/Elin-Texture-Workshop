# Puts an "Elin Texture Workshop" shortcut on the desktop pointing at the copy of the
# application sitting next to this script.
#
# Nothing is installed and nothing is written outside your desktop: a .lnk is just a
# small file holding a path. Delete it whenever you like; the application does not
# care and nothing else changes.
$ErrorActionPreference = 'Stop'

# When run from the publish folder the exe is beside the script; when run from a source
# checkout it is one level up in publish\win-x64.
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$candidates = @(
    (Join-Path $here 'ElinTextureWorkshop.exe'),
    (Join-Path (Split-Path -Parent $here) 'publish\win-x64\ElinTextureWorkshop.exe')
)

$exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    Write-Host "Could not find ElinTextureWorkshop.exe next to this script." -ForegroundColor Red
    Write-Host "Keep this script in the same folder as the application and run it again."
    Read-Host "Press Enter to close"
    exit 1
}

$exe = (Resolve-Path $exe).Path
$link = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Elin Texture Workshop.lnk'

$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($link)
$sc.TargetPath       = $exe
$sc.WorkingDirectory = Split-Path -Parent $exe
$sc.IconLocation     = "$exe,0"
$sc.Description      = 'Browse and manage Elin texture mods'
$sc.Save()

Write-Host ""
Write-Host "  Shortcut created on your desktop." -ForegroundColor Green
Write-Host "  -> $link"
Write-Host "  points at $exe"
Write-Host ""
Read-Host "Press Enter to close"
