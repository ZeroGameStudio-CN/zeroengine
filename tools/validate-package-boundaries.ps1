Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/package.json')
if ($packageFiles.Count -eq 0) {
    throw 'No ZeroEngine package.json files found.'
}

$packageByDir = @{}
$packageVersions = @{}
$assemblyPackages = @{}
$definedAssembliesByPackage = @{}

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $packageJson = Get-Content -LiteralPath $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $packageName = [string]$packageJson.name

    $deps = @{}
    $dependenciesProperty = $packageJson.PSObject.Properties['dependencies']
    if ($null -ne $dependenciesProperty) {
        foreach ($dependency in $dependenciesProperty.Value.PSObject.Properties) {
            $deps[[string]$dependency.Name] = [string]$dependency.Value
        }
    }

    $packageByDir[$packageDir] = [pscustomobject]@{
        Name = $packageName
        Version = [string]$packageJson.version
        Dependencies = $deps
    }
    $packageVersions[$packageName] = [string]$packageJson.version
    $definedAssembliesByPackage[$packageName] = New-Object System.Collections.Generic.HashSet[string]

    $asmdefFiles = @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.asmdef" "$packageDir/**/*.asmdef")
    foreach ($asmdefFile in $asmdefFiles) {
        $asmdefJson = Get-Content -LiteralPath $asmdefFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $asmdefJson.name) {
            continue
        }

        $assemblyName = [string]$asmdefJson.name
        [void]$definedAssembliesByPackage[$packageName].Add($assemblyName)

        if (-not $assemblyPackages.ContainsKey($assemblyName)) {
            $assemblyPackages[$assemblyName] = New-Object System.Collections.Generic.HashSet[string]
        }

        [void]$assemblyPackages[$assemblyName].Add($packageName)
    }
}

$issues = New-Object System.Collections.Generic.List[object]
$duplicateAssemblies = New-Object System.Collections.Generic.List[object]

foreach ($assemblyName in $assemblyPackages.Keys) {
    if ($assemblyPackages[$assemblyName].Count -gt 1) {
        $duplicateAssemblies.Add([pscustomobject]@{
            Assembly = $assemblyName
            Packages = (($assemblyPackages[$assemblyName] | Sort-Object) -join ', ')
        })
    }
}

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $packageInfo = $packageByDir[$packageDir]
    $asmdefFiles = @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.asmdef" "$packageDir/**/*.asmdef")

    foreach ($declaredDependency in ($packageInfo.Dependencies.GetEnumerator() | Sort-Object Name)) {
        $dependencyName = [string]$declaredDependency.Key
        if ($dependencyName -notlike 'com.zerogamestudio*') {
            continue
        }

        if (-not $packageVersions.ContainsKey($dependencyName)) {
            $issues.Add([pscustomobject]@{
                Package = $packageInfo.Name
                Assembly = ''
                Issue = 'unknown internal package dependency'
                Target = $dependencyName
                Declared = [string]$declaredDependency.Value
                Expected = 'local package'
            })
            continue
        }

        $expectedDependencyVersion = $packageVersions[$dependencyName]
        if ([string]$declaredDependency.Value -ne $expectedDependencyVersion) {
            $issues.Add([pscustomobject]@{
                Package = $packageInfo.Name
                Assembly = ''
                Issue = 'declared internal dependency version mismatch'
                Target = $dependencyName
                Declared = [string]$declaredDependency.Value
                Expected = $expectedDependencyVersion
            })
        }
    }

    foreach ($asmdefFile in $asmdefFiles) {
        $asmdefJson = Get-Content -LiteralPath $asmdefFile -Raw -Encoding UTF8 | ConvertFrom-Json

        if (@($asmdefJson.defineConstraints).Count -gt 0) {
            continue
        }

        foreach ($reference in @($asmdefJson.references)) {
            if (-not $reference -or -not $assemblyPackages.ContainsKey($reference)) {
                continue
            }

            if ($definedAssembliesByPackage[$packageInfo.Name].Contains([string]$reference)) {
                continue
            }

            foreach ($targetPackage in ($assemblyPackages[$reference] | Sort-Object -Unique)) {
                if ($targetPackage -eq $packageInfo.Name) {
                    continue
                }

                # The umbrella package is a compatibility surface, not a dependency
                # target for split packages.
                if ($targetPackage -eq 'com.zerogamestudio.zeroengine') {
                    continue
                }

                if (-not $packageInfo.Dependencies.ContainsKey($targetPackage)) {
                    $issues.Add([pscustomobject]@{
                        Package = $packageInfo.Name
                        Assembly = [string]$asmdefJson.name
                        Issue = 'missing package dependency'
                        Target = $targetPackage
                        Declared = ''
                        Expected = 'package dependency'
                    })
                    continue
                }

                $declaredVersion = $packageInfo.Dependencies[$targetPackage]
                $expectedVersion = $packageVersions[$targetPackage]
                if ($declaredVersion -ne $expectedVersion) {
                    $issues.Add([pscustomobject]@{
                        Package = $packageInfo.Name
                        Assembly = [string]$asmdefJson.name
                        Issue = 'dependency version mismatch'
                        Target = $targetPackage
                        Declared = $declaredVersion
                        Expected = $expectedVersion
                    })
                }
            }
        }
    }
}

if ($duplicateAssemblies.Count -gt 0) {
    Write-Warning 'Duplicate asmdef names found across packages:'
    $duplicateAssemblies | Sort-Object Assembly | Format-Table -AutoSize | Out-String | Write-Warning
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object Package, Target, Assembly | Format-Table -AutoSize
    throw 'Package boundary validation failed.'
}

Write-Host "Package boundary validation passed for $($packageFiles.Count) packages."
