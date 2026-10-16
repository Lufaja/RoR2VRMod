# Restore documented lifecycle visibility on a publicized reference copy only.
param(
    [Parameter(Mandatory)][string]$source,
    [Parameter(Mandatory)][string]$destination,
    [Parameter(Mandatory)][string]$CecilPath,
    [Parameter(Mandatory)][string]$SearchDir,
    [Parameter(Mandatory)][string]$ProvenancePath,
    [Parameter(Mandatory)][string]$ExpectedSourceSha256,
    [string]$DeclarationsPath = (Join-Path $PSScriptRoot 'ui-reference-declarations.json')
)
$ErrorActionPreference = 'Stop'
if ((Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ExpectedSourceSha256) { throw 'Unexpected publicized UI reference; refusing metadata changes.' }
if ([IO.Path]::GetFullPath($source) -eq [IO.Path]::GetFullPath($destination)) { throw 'Preserve the original reference; source and destination must differ.' }
$declarations = Get-Content $DeclarationsPath -Raw | ConvertFrom-Json
Add-Type -Path $CecilPath
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($SearchDir)
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($source, $parameters)
$changes = @()
try {
    foreach ($declaration in $declarations) {
        $type = $assembly.MainModule.Types | Where-Object FullName -EQ $declaration.type
        $matches = @($type.Methods | Where-Object { $_.Name -eq $declaration.method -and $_.Parameters.Count -eq 0 })
        if ($matches.Count -ne 1) { throw "Expected one existing lifecycle method: $($declaration.type).$($declaration.method)" }
        $method = $matches[0]
        if (-not $method.IsPublic -or -not $method.IsVirtual -or $method.ReturnType.FullName -ne 'System.Void') { throw "Unexpected input signature: $($method.FullName)" }
        $before = [int]$method.Attributes
        # Only the access bitfield changes. No method bodies, types or other member flags change.
        $method.Attributes = ($method.Attributes -band (-bnot [Mono.Cecil.MethodAttributes]::MemberAccessMask)) -bor [Mono.Cecil.MethodAttributes]::Family
        $changes += @{ member = $method.FullName; beforeAttributes = $before; afterAttributes = [int]$method.Attributes; declaration = $declaration.declaration; source = $declaration.source; sourceSnapshotSha256 = $declaration.snapshotSha256 }
    }
    $outputStream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $assembly.Write($outputStream) } finally { $outputStream.Dispose() }
} finally { $assembly.Dispose(); $resolver.Dispose() }
$provenance = @{
    package = 'RiskOfRain2.GameLibs 1.5.0-r.1'; original = $source; originalSha256 = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
    output = $destination; outputSha256 = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    purpose = 'Reference-only visibility restoration. Not pristine installed-game proof. Never distribute or load in the game.'
    methodBodiesOrTypesAdded = $false; changes = $changes
} | ConvertTo-Json -Depth 8
$provenanceStream = [IO.File]::Open($ProvenancePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($provenance + "`n")
    $provenanceStream.Write($bytes, 0, $bytes.Length)
} finally { $provenanceStream.Dispose() }
Write-Output "Restored $($changes.Count) existing protected lifecycle access flags in local UI reference only."
