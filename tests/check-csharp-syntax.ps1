param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Latest)
$options = $options.WithPreprocessorSymbols([string[]]@('UNITY_STANDALONE_WIN', 'UNITY_2021_3_OR_NEWER', 'NET_STANDARD_2_1'))
$errors = 0
$files = @('RoR2VRMod', 'VREnabler', 'VRAPI') | ForEach-Object {
    Get-ChildItem (Join-Path $RepoRoot $_) -Filter '*.cs' -Recurse -File | Where-Object { $_.FullName -notmatch '[/\\](obj|bin)[/\\]' }
}
foreach ($file in $files) {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file.FullName), $options, $file.FullName)
    foreach ($diagnostic in $tree.GetDiagnostics()) {
        if ($diagnostic.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) {
            Write-Output $diagnostic.ToString()
            $errors++
        }
    }
}
if ($errors) { throw "$errors C# syntax errors" }
Write-Output "C# syntax checked: $($files.Count) source files. Game-reference/type checking and VR execution are NOT covered."
