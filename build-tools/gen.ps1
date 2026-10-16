# Generate private hooks from the user's original, unmodified game assembly.
param(
    [Parameter(Mandatory)][string]$GameManaged,
    [Parameter(Mandatory)][string]$MonoModDir,
    [Parameter(Mandatory)][string]$OutPath
)
$ErrorActionPreference = 'Stop'
$MonoModDir = (Resolve-Path $MonoModDir).Path
$GameManaged = (Resolve-Path $GameManaged).Path
foreach ($name in @('Mono.Cecil.dll', 'MonoMod.Utils.dll', 'MonoMod.RuntimeDetour.dll')) {
    Add-Type -Path (Join-Path $MonoModDir $name) | Out-Null
}
[System.Reflection.Assembly]::LoadFrom((Join-Path $MonoModDir 'MonoMod.exe')) | Out-Null
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $MonoModDir 'MonoMod.RuntimeDetour.HookGen.exe'))
$oldDepDirs = $env:MONOMOD_DEPDIRS
try {
    # MonoMod's setting uses the host path separator (semicolon on Windows).
    $env:MONOMOD_DEPDIRS = $GameManaged + [IO.Path]::PathSeparator + $MonoModDir
    New-Item -ItemType Directory -Force (Split-Path -Parent $OutPath) | Out-Null
    Remove-Item $OutPath -ErrorAction SilentlyContinue
    $prog = $asm.GetType('MonoMod.RuntimeDetour.HookGen.Program', $true)
    $main = $prog.GetMethod('Main', [System.Reflection.BindingFlags]'Static,Public,NonPublic')
    $result = $main.Invoke($null, @(,[string[]]@('--private', (Join-Path $GameManaged 'RoR2.dll'), $OutPath)))
    if (($null -ne $result -and $result -ne 0) -or -not (Test-Path $OutPath)) {
        throw 'HookGen did not produce MMHOOK_RoR2.dll; inspect MonoMod dependencies above.'
    }
} finally { $env:MONOMOD_DEPDIRS = $oldDepDirs }
