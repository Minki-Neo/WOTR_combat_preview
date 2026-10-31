param([Parameter(Mandatory=$true)][string]$GameDir, [string]$UmmDir = "")
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$stageRoot = $null
try {
    $argsBuild = @('build', 'CombatPreview.csproj', '-c', 'Release', "-p:GameDir=$GameDir")
    if ($UmmDir) { $argsBuild += "-p:UmmDir=$UmmDir" }
    & dotnet @argsBuild
    if ($LASTEXITCODE -ne 0) { throw 'Game build failed. No package was created.' }
    $stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("CombatPreview-" + [guid]::NewGuid())
    $stage = Join-Path $stageRoot 'CombatPreview'
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item 'bin/Release/net472/CombatPreview.dll', 'Info.json', 'LICENSE', 'THIRD_PARTY.md', 'README.ko.md', 'docs/DIAGNOSTICS.ko.md' $stage
    $version = (Get-Content 'Info.json' -Raw | ConvertFrom-Json).Version
    New-Item -ItemType Directory -Force 'dist' | Out-Null
    Compress-Archive -Path $stage -DestinationPath "dist/CombatPreview-$version-beta-UMM.zip" -Force
    Write-Host 'Built read-only beta UMM package. Partial model; in-game UI validation remains required.'
} finally {
    if ($stageRoot -and (Test-Path $stageRoot)) { Remove-Item -Recurse -Force $stageRoot }
    Pop-Location
}
