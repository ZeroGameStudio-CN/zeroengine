Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$docPatterns = @(
    '*.md',
    'com.zerogamestudio.*/**/*.md',
    'docs/**/*.md',
    'manifest.json',
    'packages-lock.json'
)

$docFiles = @(git ls-files --cached --others --exclude-standard -- $docPatterns |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

if ($docFiles.Count -eq 0) {
    throw 'No consumer-facing documentation files found.'
}

$issues = New-Object System.Collections.Generic.List[string]

foreach ($file in $docFiles) {
    $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8

    if ($text -match 'github\.com/liuzqk/zeroengine\.git\?path=[^\s"''<>]+#(?:main|master|develop)\b') {
        $issues.Add("${file}: ZeroEngine Git UPM URLs must pin a tested commit, not a moving branch.")
    }

    if ($text -match 'Assets[/\\]ZeroEngine[/\\]') {
        $issues.Add("${file}: consumer-facing docs must not describe ZeroEngine package files under Assets/ZeroEngine.")
    }
}

$consumerSetupPath = 'docs/consumer-project-setup.md'
if (-not (Test-Path -LiteralPath $consumerSetupPath)) {
    $issues.Add("${consumerSetupPath}: standard consumer setup guide is missing.")
} else {
    $consumerSetup = Get-Content -LiteralPath $consumerSetupPath -Raw -Encoding UTF8
    if ($consumerSetup -notmatch 'github\.com/liuzqk/zeroengine\.git\?path=<package-directory>#<tested-commit>') {
        $issues.Add("${consumerSetupPath}: standard Git UPM URL template is missing.")
    }

    if ($consumerSetup -notmatch 'Do not commit `file:`') {
        $issues.Add("${consumerSetupPath}: temporary file dependency warning is missing.")
    }
}

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/package.json' |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $readmePath = Join-Path $packageDir 'README.md'
    if (-not (Test-Path -LiteralPath $readmePath)) {
        continue
    }

    $packageJson = Get-Content -LiteralPath $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $zeroEngineDependencies = @()
    $dependenciesProperty = $packageJson.PSObject.Properties['dependencies']
    if ($null -ne $dependenciesProperty) {
        $zeroEngineDependencies = @($dependenciesProperty.Value.PSObject.Properties |
            Where-Object { $_.Name -like 'com.zerogamestudio*' } |
            ForEach-Object { $_.Name })
    }

    if ($zeroEngineDependencies.Count -eq 0) {
        continue
    }

    $readme = Get-Content -LiteralPath $readmePath -Raw -Encoding UTF8
    if ($readme -notmatch 'same tested commit' -or $readme -notmatch 'Consumer Project Setup') {
        $issues.Add("${readmePath}: packages with com.zerogamestudio dependencies must document same tested commit pinning and link Consumer Project Setup.")
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Consumer install documentation validation failed.'
}

Write-Host "Consumer install documentation validation passed for $($docFiles.Count) files."
