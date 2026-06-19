Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-DottedPrefix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Prefix
    )

    return $Value -eq $Prefix -or $Value.StartsWith("$Prefix.", [System.StringComparison]::Ordinal)
}

function Test-DisplayPrefix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Prefix
    )

    return $Value -eq $Prefix -or
        $Value.StartsWith("$Prefix.", [System.StringComparison]::Ordinal) -or
        $Value.StartsWith("$Prefix ", [System.StringComparison]::Ordinal)
}

function Test-MenuPrefix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Prefix
    )

    return $Value -eq $Prefix -or
        $Value.StartsWith("$Prefix/", [System.StringComparison]::Ordinal) -or
        $Value.StartsWith("$Prefix ", [System.StringComparison]::Ordinal)
}

function Test-KnownDebtPrefix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageName,

        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [hashtable]$KnownDebtPrefixes
    )

    if (-not $KnownDebtPrefixes.ContainsKey($PackageName)) {
        return $false
    }

    foreach ($prefix in @($KnownDebtPrefixes[$PackageName])) {
        if (Test-DottedPrefix -Value $Value -Prefix $prefix) {
            return $true
        }
    }

    return $false
}

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/package.json' |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

if ($packageFiles.Count -eq 0) {
    throw 'No ZeroGameStudio package.json files found.'
}

$studioServicePackages = @{
    'com.zerogamestudio.analytics' = @{
        DisplayPrefix = 'ZGS Analytics'
        SymbolPrefix = 'ZGS.Analytics'
        MenuPrefix = 'ZGS/Analytics'
    }
}

$knownDebtPrefixes = @{
    'com.zerogamestudio.zeroengine.data-toolkit' = @('ZGS.DataToolkit')
}

$issues = New-Object System.Collections.Generic.List[string]
$knownDebt = New-Object System.Collections.Generic.List[string]
$asmdefCount = 0
$testAsmdefCount = 0
$namespaceCount = 0
$menuItemCount = 0

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $packageJson = Get-Content -LiteralPath $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $packageName = [string]$packageJson.name
    $displayName = [string]$packageJson.displayName

    $isZeroEnginePackage = $packageName -eq 'com.zerogamestudio.zeroengine' -or
        $packageName.StartsWith('com.zerogamestudio.zeroengine.', [System.StringComparison]::Ordinal)
    $isStudioServicePackage = $studioServicePackages.ContainsKey($packageName)

    if ($isZeroEnginePackage) {
        if (-not (Test-DisplayPrefix -Value $displayName -Prefix 'ZeroEngine')) {
            $issues.Add("$packageFile displayName '$displayName' must start with ZeroEngine for $packageName")
        }
    } elseif ($isStudioServicePackage) {
        $service = $studioServicePackages[$packageName]
        if (-not (Test-DisplayPrefix -Value $displayName -Prefix $service.DisplayPrefix)) {
            $issues.Add("$packageFile displayName '$displayName' must start with $($service.DisplayPrefix) for $packageName")
        }
    } elseif ($packageName.StartsWith('com.zerogamestudio.', [System.StringComparison]::Ordinal)) {
        $issues.Add("$packageFile uses unclassified naming lane for package '$packageName'. Add an explicit lane rule before shipping.")
    }

    $asmdefFiles = @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.asmdef" "$packageDir/**/*.asmdef" |
        Where-Object { Test-Path -LiteralPath $_ } |
        Sort-Object -Unique)

    foreach ($asmdefFile in $asmdefFiles) {
        $normalizedAsmdefPath = $asmdefFile -replace '\\', '/'
        if ($normalizedAsmdefPath -match '/Tests/') {
            # This batch does not rename test assemblies. Production/editor
            # assemblies are the package surface enforced here; test assembly
            # visibility remains covered by validate-test-assemblies.ps1.
            $testAsmdefCount++
            continue
        }

        $asmdefCount++
        $asmdefJson = Get-Content -LiteralPath $asmdefFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $assemblyName = [string]$asmdefJson.name
        $rootNamespaceProperty = $asmdefJson.PSObject.Properties['rootNamespace']
        $rootNamespace = ''
        if ($null -ne $rootNamespaceProperty) {
            $rootNamespace = [string]$rootNamespaceProperty.Value
        }

        if ($isZeroEnginePackage) {
            if (-not (Test-DottedPrefix -Value $assemblyName -Prefix 'ZeroEngine')) {
                if (Test-KnownDebtPrefix -PackageName $packageName -Value $assemblyName -KnownDebtPrefixes $knownDebtPrefixes) {
                    $knownDebt.Add("$asmdefFile assembly '$assemblyName' is allowed known debt for $packageName")
                } else {
                    $issues.Add("$asmdefFile assembly name '$assemblyName' must use ZeroEngine.* for $packageName")
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($rootNamespace) -and
                -not (Test-DottedPrefix -Value $rootNamespace -Prefix 'ZeroEngine')) {
                if (Test-KnownDebtPrefix -PackageName $packageName -Value $rootNamespace -KnownDebtPrefixes $knownDebtPrefixes) {
                    $knownDebt.Add("$asmdefFile rootNamespace '$rootNamespace' is allowed known debt for $packageName")
                } else {
                    $issues.Add("$asmdefFile rootNamespace '$rootNamespace' must use ZeroEngine.* for $packageName")
                }
            }
        } elseif ($isStudioServicePackage) {
            $service = $studioServicePackages[$packageName]
            if (-not (Test-DottedPrefix -Value $assemblyName -Prefix $service.SymbolPrefix)) {
                $issues.Add("$asmdefFile assembly name '$assemblyName' must use $($service.SymbolPrefix).* for $packageName")
            }

            if (-not [string]::IsNullOrWhiteSpace($rootNamespace) -and
                -not (Test-DottedPrefix -Value $rootNamespace -Prefix $service.SymbolPrefix)) {
                $issues.Add("$asmdefFile rootNamespace '$rootNamespace' must use $($service.SymbolPrefix).* for $packageName")
            }
        }
    }

    $sourceFiles = @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.cs" "$packageDir/**/*.cs" |
        Where-Object { Test-Path -LiteralPath $_ } |
        Sort-Object -Unique)

    foreach ($sourceFile in $sourceFiles) {
        $sourceText = Get-Content -LiteralPath $sourceFile -Raw -Encoding UTF8
        $namespaceMatches = [regex]::Matches($sourceText, '(?m)^\s*namespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\b')

        foreach ($namespaceMatch in $namespaceMatches) {
            $namespaceCount++
            $namespaceName = $namespaceMatch.Groups['namespace'].Value

            if ($isZeroEnginePackage) {
                if (-not (Test-DottedPrefix -Value $namespaceName -Prefix 'ZeroEngine')) {
                    if (Test-KnownDebtPrefix -PackageName $packageName -Value $namespaceName -KnownDebtPrefixes $knownDebtPrefixes) {
                        $knownDebt.Add("$sourceFile namespace '$namespaceName' is allowed known debt for $packageName")
                    } else {
                        $issues.Add("$sourceFile namespace '$namespaceName' must use ZeroEngine.* for $packageName")
                    }
                }
            } elseif ($isStudioServicePackage) {
                $service = $studioServicePackages[$packageName]
                if (-not (Test-DottedPrefix -Value $namespaceName -Prefix $service.SymbolPrefix)) {
                    $issues.Add("$sourceFile namespace '$namespaceName' must use $($service.SymbolPrefix).* for $packageName")
                }
            }
        }

        $nonLiteralMenuMatches = [regex]::Matches($sourceText, '\[\s*MenuItem\s*\(\s*(?!@?")(?<argument>[^,\)\r\n]+)')
        foreach ($nonLiteralMenuMatch in $nonLiteralMenuMatches) {
            $menuItemCount++
            $menuArgument = $nonLiteralMenuMatch.Groups['argument'].Value.Trim()
            $issues.Add("$sourceFile MenuItem path must be a string literal so package ownership can be validated; found '$menuArgument'")
        }

        $menuMatches = [regex]::Matches($sourceText, '\[\s*MenuItem\s*\(\s*@?"(?<path>(?:\\.|[^"\\])*)"')
        foreach ($menuMatch in $menuMatches) {
            $menuItemCount++
            $menuPath = $menuMatch.Groups['path'].Value

            if (Test-MenuPrefix -Value $menuPath -Prefix 'Assets') {
                continue
            }

            if ($isZeroEnginePackage) {
                if (-not (Test-MenuPrefix -Value $menuPath -Prefix 'ZeroEngine')) {
                    $issues.Add("$sourceFile MenuItem '$menuPath' must use ZeroEngine/... for $packageName")
                }
            } elseif ($isStudioServicePackage) {
                $service = $studioServicePackages[$packageName]
                if (-not (Test-MenuPrefix -Value $menuPath -Prefix $service.MenuPrefix)) {
                    $issues.Add("$sourceFile MenuItem '$menuPath' must use $($service.MenuPrefix)/... for $packageName")
                }
            }
        }
    }
}

if ($knownDebt.Count -gt 0) {
    Write-Host 'Known naming ownership debt allowlisted:'
    $knownDebt | Sort-Object -Unique | ForEach-Object { Write-Host "  - $_" }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Package naming ownership validation failed.'
}

Write-Host "Package naming ownership validation passed for $($packageFiles.Count) packages, $asmdefCount production/editor asmdefs, $namespaceCount C# namespace declarations, and $menuItemCount MenuItem entries. Skipped $testAsmdefCount test asmdefs."
