param(
    [Parameter(Mandatory)][string]$asmPath,
    [Parameter(Mandatory)][string]$typeName,
    [Parameter(Mandatory)][string]$methodName,
    [Parameter(Mandatory)][string]$outFile,
    [Parameter(Mandatory)][string]$CecilPath,
    [string[]]$searchDirs
)
$ErrorActionPreference = 'Stop'
Add-Type -Path $CecilPath | Out-Null
$r = New-Object Mono.Cecil.DefaultAssemblyResolver
$r.AddSearchDirectory((Split-Path -Parent $asmPath))
foreach ($directory in $searchDirs) { $r.AddSearchDirectory($directory) }
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.AssemblyResolver=$r
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath,$rp)
$t=$a.MainModule.GetTypes() | ? { $_.FullName -eq $typeName }
if(-not $t){ "MISSING TYPE $typeName" | Out-File $outFile; exit 1 }
$sb = New-Object System.Text.StringBuilder
foreach($m in ($t.Methods | ? { $_.Name -eq $methodName })){
  [void]$sb.AppendLine("== $($m.FullName)")
  if(-not $m.HasBody){ [void]$sb.AppendLine("  (no body)"); continue }
  foreach($v in $m.Body.Variables){ [void]$sb.AppendLine("  local V_$($v.Index): $($v.VariableType.FullName)") }
  foreach($i in $m.Body.Instructions){ [void]$sb.AppendLine(("  IL_{0:x4}: {1} {2}" -f $i.Offset, $i.OpCode.Name, $i.Operand)) }
}
$sb.ToString() | Out-File $outFile

$a.Dispose()
$r.Dispose()
