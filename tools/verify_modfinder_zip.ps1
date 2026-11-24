param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Tag
)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v?(\d+\.\d+\.\d+)$') { throw 'Expected a numeric release tag, such as v0.5.25.' }
$version = $Matches[1]
if ((Split-Path -Leaf $Archive) -notin @("CombatPreview-$version-beta-UMM.zip", "CombatPreview-$version-diagnostic-only-UMM.zip")) {
    throw 'Select the versioned UMM installation ZIP.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).Path)
try {
    $allowed = @('CombatPreview.dll','Info.json','LICENSE','THIRD_PARTY.md','README.ko.md','DIAGNOSTICS.ko.md') |
        ForEach-Object { "CombatPreview/$_" }
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    if ($entries.Count -ne ($entries | Select-Object -Unique).Count -or
        @($entries | Where-Object { $_ -cnotin $allowed }).Count -gt 0) { throw 'Unexpected or duplicate ZIP entries.' }
    $infoEntry = $zip.GetEntry('CombatPreview/Info.json')
    $dllEntry = $zip.GetEntry('CombatPreview/CombatPreview.dll')
    if ($null -eq $infoEntry -or $null -eq $dllEntry) { throw 'Missing Info.json or DLL.' }
    $reader = [IO.StreamReader]::new($infoEntry.Open())
    try { $info = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($info.Version -ne $version -or $info.Id -cne 'CombatPreview' -or $info.AssemblyName -cne 'CombatPreview.dll') {
        throw 'Tag, version, mod ID or assembly name mismatch.'
    }
    $temp = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $Archive).Path) ("CombatPreview-check-" + [guid]::NewGuid() + '.dll')
    try {
        [IO.Compression.ZipFileExtensions]::ExtractToFile($dllEntry, $temp)
        $assembly = [Reflection.AssemblyName]::GetAssemblyName($temp)
        if ($assembly.Name -cne 'CombatPreview' -or $assembly.Version.ToString(3) -ne $version) {
            throw 'Packaged DLL version differs from Info.json and the tag.'
        }
        Write-Output "PASS: CombatPreview $version, DLL $($assembly.Version)"
    } finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
    }
} finally { $zip.Dispose() }
