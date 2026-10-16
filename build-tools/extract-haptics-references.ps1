param([string]$AssemblyPath, [string]$Destination)
$ErrorActionPreference = 'Stop'
$stream = [IO.File]::OpenRead($AssemblyPath)
$pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $resourceRva = $pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress
    [byte[]]$data = $pe.GetSectionData($resourceRva).GetContent()
    foreach ($handle in $metadata.ManifestResources) {
        $resource = $metadata.GetManifestResource($handle)
        $name = $metadata.GetString($resource.Name)
        if ($name -match '(Bhaptics[.]Tact[.]dll|ShockwaveManager[.]dll)$') {
            if (-not $resource.Implementation.IsNil) { throw "External resource: $name" }
            $filename = $Matches[1]
            $length = [BitConverter]::ToInt32($data, [int]$resource.Offset)
            if ($length -lt 1024 -or $resource.Offset + 4 + $length -gt $data.Length) { throw 'Invalid resource range' }
            $bytes = [byte[]]::new($length)
            [Array]::Copy($data, [int]$resource.Offset + 4, $bytes, 0, $length)
            New-Item -ItemType Directory -Force $Destination | Out-Null
            $output = Join-Path $Destination $filename
            $outputStream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try { $outputStream.Write($bytes, 0, $bytes.Length) } finally { $outputStream.Dispose() }
            Write-Host "$name -> $output ($length bytes)"
        }
    }
} finally { $pe.Dispose(); $stream.Dispose() }
