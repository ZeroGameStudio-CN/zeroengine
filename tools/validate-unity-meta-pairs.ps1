Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assetPatterns = @(
    'com.zerogamestudio.*/*.cs',
    'com.zerogamestudio.*/**/*.cs',
    'com.zerogamestudio.*/*.asmdef',
    'com.zerogamestudio.*/**/*.asmdef'
)

$metaPatterns = @(
    'com.zerogamestudio.*/*.cs.meta',
    'com.zerogamestudio.*/**/*.cs.meta',
    'com.zerogamestudio.*/*.asmdef.meta',
    'com.zerogamestudio.*/**/*.asmdef.meta'
)

$allPackageMetaPatterns = @(
    'com.zerogamestudio*.meta',
    'com.zerogamestudio*/**/*.meta'
)

$assetFiles = @(git ls-files --cached --others --exclude-standard -- $assetPatterns |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

$metaFiles = @(git ls-files --cached --others --exclude-standard -- $metaPatterns |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

$allPackageMetaFiles = @(git ls-files --cached --others --exclude-standard -- $allPackageMetaPatterns |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

$issues = New-Object System.Collections.Generic.List[string]
$guidToFiles = @{}

foreach ($file in $assetFiles) {
    if (-not (Test-Path -LiteralPath "${file}.meta")) {
        $issues.Add("${file}: missing Unity .meta file.")
    }
}

foreach ($metaFile in $metaFiles) {
    $assetFile = $metaFile -replace '\.meta$', ''
    if (-not (Test-Path -LiteralPath $assetFile)) {
        $issues.Add("${metaFile}: orphan Unity .meta file without matching asset.")
    }
}

foreach ($metaFile in $allPackageMetaFiles) {
    $text = Get-Content -LiteralPath $metaFile -Raw -Encoding UTF8
    $match = [regex]::Match($text, '(?m)^guid:\s*(?<guid>[0-9a-fA-F]{32})\s*$')
    if (-not $match.Success) {
        $issues.Add("${metaFile}: Unity .meta file is missing a 32-character guid.")
        continue
    }

    $guid = $match.Groups['guid'].Value.ToLowerInvariant()
    if (-not $guidToFiles.ContainsKey($guid)) {
        $guidToFiles[$guid] = New-Object System.Collections.Generic.List[string]
    }

    $guidToFiles[$guid].Add($metaFile)
}

foreach ($entry in $guidToFiles.GetEnumerator()) {
    if ($entry.Value.Count -gt 1) {
        $issues.Add("Duplicate Unity .meta guid $($entry.Key): $($entry.Value -join ', ')")
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Unity C#/asmdef .meta pair validation failed.'
}

Write-Host "Unity C#/asmdef .meta pair validation passed: $($assetFiles.Count) assets, $($metaFiles.Count) paired meta files, $($allPackageMetaFiles.Count) package meta GUIDs."
