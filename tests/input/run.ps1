$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sources = @(
    'RoR2VRMod/Inputs/GameplayBindingCatalog.cs'
    'RoR2VRMod/Inputs/AutoSprintPulse.cs'
    'RoR2VRMod/Inputs/ButtonGestureState.cs'
    'RoR2VRMod/Inputs/InputTypes/BaseInput.cs'
    'RoR2VRMod/Inputs/InputTypes/ButtonInput.cs'
    'RoR2VRMod/Inputs/InputTypes/VectorInput.cs'
    'RoR2VRMod/Inputs/InputTypes/TapButtonInput.cs'
    'RoR2VRMod/Inputs/InputTypes/HoldableButtonInput.cs'
    'RoR2VRMod/Inputs/KeyItemCombo.cs'
    'tests/input/InputContractTests.cs'
) | ForEach-Object { Join-Path $repoRoot $_ }
Add-Type -Path $sources
[InputContractTests]::Run()
