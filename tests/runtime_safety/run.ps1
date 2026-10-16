param([string]$RestoreFixture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Add-Type -Path @((Join-Path $repo 'VREnabler/NativeFileInstaller.cs'), (Join-Path $PSScriptRoot 'NativeInstallerTests.cs'))
$count = [NativeInstallerTests]::Run()
Write-Host "$count native installer tests passed."
if ($RestoreFixture) { [NativeInstallerTests]::CreateRestoreFixture($RestoreFixture) }
