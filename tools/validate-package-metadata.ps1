Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/package.json')
if ($packageFiles.Count -eq 0) {
    throw 'No ZeroEngine package.json files found.'
}

$issues = New-Object System.Collections.Generic.List[string]

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $packageJson = Get-Content -LiteralPath $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $packageName = [string]$packageJson.name

    foreach ($property in @('name', 'version', 'displayName', 'description', 'unity', 'license', 'repository', 'author')) {
        $propertyInfo = $packageJson.PSObject.Properties[$property]
        if ($null -eq $propertyInfo -or [string]::IsNullOrWhiteSpace([string]$propertyInfo.Value)) {
            $issues.Add("$packageDir/package.json missing $property")
        }
    }

    if ($packageJson.PSObject.Properties['repository']) {
        $repository = $packageJson.repository
        if ($null -eq $repository.PSObject.Properties['type'] -or [string]$repository.type -ne 'git') {
            $issues.Add("$packageDir/package.json repository.type must be git")
        }

        if ($null -eq $repository.PSObject.Properties['url'] -or [string]::IsNullOrWhiteSpace([string]$repository.url)) {
            $issues.Add("$packageDir/package.json repository.url missing")
        }

        if ($null -eq $repository.PSObject.Properties['directory'] -or [string]$repository.directory -ne $packageDir) {
            $issues.Add("$packageDir/package.json repository.directory must match package directory")
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($packageName) -and $packageName -notlike 'com.zerogamestudio*') {
        $issues.Add("$packageDir/package.json package name must use com.zerogamestudio prefix")
    }

    $keywordsProperty = $packageJson.PSObject.Properties['keywords']
    if ($null -eq $keywordsProperty -or @($keywordsProperty.Value).Count -eq 0) {
        $issues.Add("$packageDir/package.json keywords must contain at least one package keyword")
    }

    $authorProperty = $packageJson.PSObject.Properties['author']
    if ($null -ne $authorProperty -and $null -ne $authorProperty.Value -and $authorProperty.Value -isnot [string]) {
        $authorNameProperty = $authorProperty.Value.PSObject.Properties['name']
        if ($null -eq $authorNameProperty -or [string]::IsNullOrWhiteSpace([string]$authorNameProperty.Value)) {
            $issues.Add("$packageDir/package.json author.name missing")
        }
    }

    foreach ($requiredFile in @('README.md', 'CHANGELOG.md', 'package.json.meta')) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageDir $requiredFile))) {
            $issues.Add("$packageDir missing $requiredFile")
        }
    }

    $readmePath = Join-Path $packageDir 'README.md'
    if (Test-Path -LiteralPath $readmePath) {
        $readmeText = Get-Content -LiteralPath $readmePath -Raw -Encoding UTF8
        $versionPatterns = @(
            '\*\*(?:版本|Version)\*\*:\s*(?<version>[0-9]+\.[0-9]+\.[0-9]+)',
            '(?mi)^Version:\s*(?<version>[0-9]+\.[0-9]+\.[0-9]+)'
        )
        foreach ($versionPattern in $versionPatterns) {
            $versionMatch = [regex]::Match($readmeText, $versionPattern)
            if ($versionMatch.Success -and $versionMatch.Groups['version'].Value -ne [string]$packageJson.version) {
                $issues.Add("$packageDir/README.md version $($versionMatch.Groups['version'].Value) does not match package.json version $($packageJson.version)")
            }
        }
    }

    $changelogPath = Join-Path $packageDir 'CHANGELOG.md'
    if (Test-Path -LiteralPath $changelogPath) {
        $changelogText = Get-Content -LiteralPath $changelogPath -Raw -Encoding UTF8
        if ($changelogText -notmatch '(?m)^##\s+Unreleased\s*$') {
            $issues.Add("$packageDir/CHANGELOG.md missing ## Unreleased section")
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Package metadata validation failed.'
}

Write-Host "Package metadata validation passed for $($packageFiles.Count) packages."
