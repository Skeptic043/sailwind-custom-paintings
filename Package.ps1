#requires -Version 7.0
param(
    [string]$GameManagedDir = 'C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed',
    [string]$ReferenceDir = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if (-not $SkipBuild) {
    & (Join-Path $projectRoot 'Build.ps1') -GameManagedDir $GameManagedDir -ReferenceDir $ReferenceDir
    if ($LASTEXITCODE -ne 0) { throw 'Build did not succeed.' }
}
$manifest = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'manifest.json') | ConvertFrom-Json
if ($manifest.name -cne 'Custom_Paintings') { throw 'manifest.name must be Custom_Paintings.' }
$version = $manifest.version_number
$changes = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md')
if ($changes -notmatch ('(?m)^#{1,3}\s+(?:Version\s+)?' + [regex]::Escape($version) + '(\s|$)')) {
    throw "CHANGELOG.md must have a heading for $version."
}

$releaseNames = @('manifest.json','README.md','CHANGELOG.md','LICENSE')
# Thunderstore requires icon.png for publication. Local test packages are built without one until it exists.
if (Test-Path -LiteralPath (Join-Path $projectRoot 'icon.png')) { $releaseNames += 'icon.png' }
else { Write-Warning 'icon.png is missing. The package is fine for local testing but not for Thunderstore.' }
$entries = @($releaseNames | ForEach-Object { [pscustomobject]@{ Source=(Join-Path $projectRoot $_); Name=$_ } })
$entries += [pscustomobject]@{ Source=(Join-Path $projectRoot 'bin/Release/net471/CustomPaintings.dll')
    Name='BepInEx/plugins/CustomPaintings/CustomPaintings.dll' }
foreach ($entry in $entries) {
    if (-not [IO.File]::Exists($entry.Source)) { throw "Required package input is missing: $($entry.Name)" }
}

$artifactDir = Join-Path $projectRoot 'artifacts'
[IO.Directory]::CreateDirectory($artifactDir) | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$zip = Join-Path $artifactDir "CustomPaintings-$version-thunderstore-$stamp.zip"
$temp = "$zip.partial"
try {
    $stream = [IO.File]::Open($temp, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,
                $entry.Source, $entry.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
    # Read every member back and compare its name and bytes to the inputs.
    $check = [IO.Compression.ZipFile]::OpenRead($temp)
    try {
        if ($check.Entries.Count -ne $entries.Count) { throw 'Unexpected entry count in the package.' }
        foreach ($entry in $entries) {
            $member = $check.GetEntry($entry.Name)
            if ($null -eq $member) { throw "Package is missing $($entry.Name)." }
            $memory = [IO.MemoryStream]::new()
            $reader = $member.Open()
            try { $reader.CopyTo($memory) } finally { $reader.Dispose() }
            $expected = [IO.File]::ReadAllBytes($entry.Source)
            if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$memory.ToArray(), [byte[]]$expected)) {
                throw "Package member differs from its input: $($entry.Name)"
            }
        }
    } finally { $check.Dispose() }
    [IO.File]::Move($temp, $zip)
    [pscustomobject]@{ Path=$zip; SHA256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash }
} finally {
    if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
}
