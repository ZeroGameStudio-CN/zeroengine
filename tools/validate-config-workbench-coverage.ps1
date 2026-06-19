Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PackageDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (($Path -replace '\\', '/') -split '/')[0]
}

function Test-ZeroEngineNamespace {
    param([string]$Namespace)
    return -not [string]::IsNullOrWhiteSpace($Namespace) -and
        ($Namespace.StartsWith('ZeroEngine.', [System.StringComparison]::Ordinal) -or
            $Namespace.StartsWith('ZGS.', [System.StringComparison]::Ordinal))
}

$sourceFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/*.cs' 'com.zerogamestudio.*/**/*.cs' |
    Where-Object {
        $_ -notmatch '/Tests/' -and
        ($_ -match '/Runtime/' -or $_ -match '/Editor/')
    })
if ($sourceFiles.Count -eq 0) {
    throw 'No ZeroEngine package source files found.'
}

$issues = New-Object System.Collections.Generic.List[string]
$configAssets = New-Object System.Collections.Generic.List[object]
$indirectBaseTypes = New-Object System.Collections.Generic.List[object]
$configValidatorFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/*ConfigValidator.cs' 'com.zerogamestudio.*/**/*ConfigValidator.cs' |
    Where-Object {
        $_ -notmatch '/Tests/' -and
        $_ -match '/Editor/'
    })

foreach ($file in $sourceFiles) {
    $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8
    $matches = [regex]::Matches(
        $text,
        '(?s)\[CreateAssetMenu(?<attribute>[^\]]*)\]\s*(?:\[[^\]]+\]\s*)*(?:public|internal|private)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<base>[A-Za-z_][A-Za-z0-9_\.]*)')

    foreach ($match in $matches) {
        $typeName = [string]$match.Groups['type'].Value
        $baseType = [string]$match.Groups['base'].Value
        $attribute = [string]$match.Groups['attribute'].Value
        $packageDir = Get-PackageDirectory $file

        if ($baseType -ne 'ScriptableObject' -and
            $baseType -ne 'SerializedScriptableObject' -and
            -not $baseType.EndsWith('.ScriptableObject', [System.StringComparison]::Ordinal) -and
            -not $baseType.EndsWith('.SerializedScriptableObject', [System.StringComparison]::Ordinal)) {
            $indirectBaseTypes.Add([pscustomobject]@{
                Type = $typeName
                BaseType = $baseType
                File = $file
            })
        }

        if ($attribute -notmatch 'menuName\s*=') {
            $issues.Add("${file}: $typeName CreateAssetMenu is missing menuName.")
        } elseif ($attribute -notmatch 'menuName\s*=\s*""?(ZeroEngine|ZGS)[/\\]') {
            $issues.Add("${file}: $typeName CreateAssetMenu menuName should start with ZeroEngine/ or ZGS/.")
        }

        $configAssets.Add([pscustomobject]@{
            Package = $packageDir
            Type = $typeName
            BaseType = $baseType
            File = $file
        })
    }
}

if ($configAssets.Count -eq 0) {
    throw 'No CreateAssetMenu ScriptableObject config assets found. DataToolkit coverage cannot be verified.'
}

if (-not (Test-Path -LiteralPath 'com.zerogamestudio.zeroengine.data-toolkit/Editor/Windows/DataToolkitWindow.cs')) {
    $issues.Add('DataToolkit window is missing; shared config workbench coverage is unavailable.')
}

if (-not (Test-Path -LiteralPath 'com.zerogamestudio.zeroengine.data-toolkit/Editor/Validation/DataAssetValidationService.cs')) {
    $issues.Add('DataToolkit validation service is missing; shared config validation coverage is unavailable.')
}

if (-not (Test-Path -LiteralPath 'com.zerogamestudio.zeroengine.data-toolkit/Editor/Validation/PackageConfigValidationProvider.cs')) {
    $issues.Add('DataToolkit package config validation bridge is missing; package-local validators are not surfaced in the shared report.')
}

$registryPath = 'com.zerogamestudio.zeroengine.data-toolkit/Editor/Core/DataToolkitProjectRegistry.cs'
if (-not (Test-Path -LiteralPath $registryPath)) {
    $issues.Add('DataToolkit project registry is missing; validation provider discovery cannot be verified.')
} else {
    $registryText = Get-Content -LiteralPath $registryPath -Raw -Encoding UTF8
    if ($registryText -notmatch 'GetTypesDerivedFrom\s*<\s*IDataToolkitValidationProvider\s*>') {
        $issues.Add("${registryPath}: generic profile must discover IDataToolkitValidationProvider implementations.")
    }
}

$validatorPackages = @{}
foreach ($validatorFile in $configValidatorFiles) {
    $packageDir = Get-PackageDirectory $validatorFile
    if (-not $validatorPackages.ContainsKey($packageDir)) {
        $validatorPackages[$packageDir] = New-Object System.Collections.Generic.List[string]
    }

    $validatorPackages[$packageDir].Add($validatorFile)
}

foreach ($packageDir in @($configAssets | ForEach-Object { $_.Package } | Sort-Object -Unique)) {
    if (-not $validatorPackages.ContainsKey($packageDir) -or $validatorPackages[$packageDir].Count -eq 0) {
        $issues.Add("${packageDir}: package has CreateAssetMenu config assets but no package-local Editor/*ConfigValidator.cs.")
    }
}

foreach ($validatorFile in $configValidatorFiles) {
    $text = Get-Content -LiteralPath $validatorFile -Raw -Encoding UTF8
    $packageDir = Get-PackageDirectory $validatorFile
    $validatorClassName = [System.IO.Path]::GetFileNameWithoutExtension($validatorFile)
    $escapedValidatorClassName = [regex]::Escape($validatorClassName)
    if ($text -notmatch "\bclass\s+$escapedValidatorClassName\b") {
        $issues.Add("${validatorFile}: package ConfigValidator class name must match the file name '$validatorClassName'.")
    }

    $packageTestPatterns = @(
        "$packageDir/Tests/*.cs",
        "$packageDir/Tests/**/*.cs"
    )
    $packageTestFiles = @(git ls-files --cached --others --exclude-standard -- $packageTestPatterns |
        Where-Object { Test-Path -LiteralPath $_ })
    $hasValidatorTestCoverage = $false
    foreach ($packageTestFile in $packageTestFiles) {
        $testText = Get-Content -LiteralPath $packageTestFile -Raw -Encoding UTF8
        if ($testText -match "\b$escapedValidatorClassName\b") {
            $hasValidatorTestCoverage = $true
            break
        }
    }

    if (-not $hasValidatorTestCoverage) {
        $issues.Add("${validatorFile}: package ConfigValidator must be referenced by package-local tests.")
    }

    $namespaceMatch = [regex]::Match($text, 'namespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)')
    $namespaceName = if ($namespaceMatch.Success) { [string]$namespaceMatch.Groups['namespace'].Value } else { '' }
    if (-not (Test-ZeroEngineNamespace $namespaceName)) {
        $issues.Add("${validatorFile}: package ConfigValidator namespace must start with ZeroEngine. or ZGS. so DataToolkit can bridge it safely.")
    }

    $validateMatches = [regex]::Matches(
        $text,
        '(?s)public\s+static\s+IReadOnlyList\s*<[^>]+>\s+Validate\s*\((?<parameters>.*?)\)')
    if ($validateMatches.Count -eq 0) {
        $issues.Add("${validatorFile}: package ConfigValidator must expose public static IReadOnlyList<TIssue> Validate(IEnumerable<TConfig>...) for DataToolkit bridge coverage.")
    }

    foreach ($validateMatch in $validateMatches) {
        $parameters = [string]$validateMatch.Groups['parameters'].Value
        $parameterParts = @($parameters -split ',' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($parameterParts.Count -eq 0) {
            $issues.Add("${validatorFile}: package ConfigValidator Validate method must take at least one IEnumerable<TConfig> parameter.")
            continue
        }

        foreach ($parameter in $parameterParts) {
            if ($parameter -notmatch '(?:^|\s)(?:System\.Collections\.Generic\.)?IEnumerable\s*<\s*[A-Za-z_][A-Za-z0-9_.]*\s*>') {
                $issues.Add("${validatorFile}: DataToolkit bridge only supports Validate parameters shaped as IEnumerable<TConfig>; unsupported parameter '$parameter'.")
            }
        }
    }

    if ($text -notmatch '\bSeverity\b') {
        $issues.Add("${validatorFile}: validation issues must expose Severity for DataToolkit report mapping.")
    }

    if ($text -notmatch '\bFieldPath\b') {
        $issues.Add("${validatorFile}: validation issues must expose FieldPath for designer-facing report mapping.")
    }

    if ($text -notmatch '\bMessage\b') {
        $issues.Add("${validatorFile}: validation issues must expose Message for designer-facing report mapping.")
    }

    if ($text -notmatch '\bAssetName\b' -and $text -notmatch '\bAsset\b') {
        $issues.Add("${validatorFile}: validation issues must expose Asset or AssetName so DataToolkit can map issues back to assets.")
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Config workbench coverage validation failed.'
}

$summary = $configAssets |
    Group-Object Package |
    Sort-Object Name |
    ForEach-Object { '{0}: {1}' -f $_.Name, $_.Count }

Write-Host "Config workbench coverage validation passed: $($configAssets.Count) CreateAssetMenu config types."
Write-Host "  Package ConfigValidator files: $($configValidatorFiles.Count)"
if ($indirectBaseTypes.Count -gt 0) {
    Write-Host "  Indirect or external ScriptableObject bases: $($indirectBaseTypes.Count)"
}
$summary | ForEach-Object { Write-Host "  $_" }
