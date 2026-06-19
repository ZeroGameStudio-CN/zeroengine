Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio*' |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

if ($packageFiles.Count -eq 0) {
    throw 'No ZeroEngine package files found.'
}

$forbiddenPathSegments = @(
    '/.claude/',
    '/.codex/',
    '/.cursor/',
    '/.idea/',
    '/.vscode/'
)

$forbiddenFilePatterns = @(
    ',(\.meta)?$',
    '~(\.meta)?$',
    '\.(bak|orig|rej|tmp|swp)(\.meta)?$',
    'Thumbs\.db$',
    '\.DS_Store$'
)

$issues = New-Object System.Collections.Generic.List[string]

foreach ($file in $packageFiles) {
    $normalizedPath = $file -replace '\\', '/'

    foreach ($segment in $forbiddenPathSegments) {
        if ($normalizedPath.IndexOf($segment, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $issues.Add("${file}: package contains local agent/editor workspace path '$segment'. Move it outside the UPM package.")
        }
    }

    $fileName = [System.IO.Path]::GetFileName($normalizedPath)
    foreach ($pattern in $forbiddenFilePatterns) {
        if ($fileName -match $pattern) {
            $issues.Add("${file}: package contains temporary or backup-looking file name.")
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Package shipping cleanliness validation failed.'
}

Write-Host "Package shipping cleanliness validation passed for $($packageFiles.Count) files."
