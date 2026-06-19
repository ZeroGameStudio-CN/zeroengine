Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyToPackage = @{
    'Unity.Addressables' = 'com.unity.addressables'
    'Unity.ResourceManager' = 'com.unity.addressables'
    'Unity.InputSystem' = 'com.unity.inputsystem'
    'Unity.Localization' = 'com.unity.localization'
    'Unity.TextMeshPro' = 'com.unity.textmeshpro'
    'Unity.Netcode.Runtime' = 'com.unity.netcode.gameobjects'
    'Unity.Networking.Transport' = 'com.unity.transport'
    'Unity.Services.Core' = 'com.unity.services.core'
    'Unity.Services.Authentication' = 'com.unity.services.authentication'
    'Unity.Services.Lobbies' = 'com.unity.services.lobby'
    'Unity.Services.Relay' = 'com.unity.services.relay'
}

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/package.json')
$issues = New-Object System.Collections.Generic.List[object]

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $packageJson = Get-Content -LiteralPath $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $dependencies = @{}
    $dependencyProperty = $packageJson.PSObject.Properties['dependencies']
    if ($null -ne $dependencyProperty) {
        foreach ($dependency in $dependencyProperty.Value.PSObject.Properties) {
            $dependencies[[string]$dependency.Name] = [string]$dependency.Value
        }
    }

    $asmdefFiles = @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.asmdef" "$packageDir/**/*.asmdef")
    foreach ($asmdefFile in $asmdefFiles) {
        $asmdef = Get-Content -LiteralPath $asmdefFile -Raw -Encoding UTF8 | ConvertFrom-Json

        if (@($asmdef.defineConstraints).Count -gt 0) {
            continue
        }

        foreach ($reference in @($asmdef.references)) {
            if (-not $reference -or -not $assemblyToPackage.ContainsKey($reference)) {
                continue
            }

            $requiredPackage = $assemblyToPackage[$reference]
            if (-not $dependencies.ContainsKey($requiredPackage)) {
                $issues.Add([pscustomobject]@{
                    Package = [string]$packageJson.name
                    Assembly = [string]$asmdef.name
                    Reference = [string]$reference
                    MissingPackage = $requiredPackage
                })
            }
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object Package, MissingPackage, Assembly | Format-Table -AutoSize
    throw 'Unity package dependency validation failed.'
}

Write-Host "Unity package dependency validation passed for $($packageFiles.Count) packages."
