Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$knownUnityAssemblies = @(
    'Unity.Addressables',
    'Unity.ResourceManager',
    'Unity.InputSystem',
    'Unity.Localization',
    'Unity.TextMeshPro',
    'Unity.Netcode.Runtime',
    'Unity.Networking.Transport',
    'Unity.Services.Core',
    'Unity.Services.Authentication',
    'Unity.Services.Lobbies',
    'Unity.Services.Relay',
    'Unity.ugui',
    'UnityEditor.TestRunner',
    'UnityEngine.TestRunner'
)

$packageFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/package.json')
$internalAssemblies = @{}

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    foreach ($asmdefFile in @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.asmdef" "$packageDir/**/*.asmdef")) {
        $asmdef = Get-Content -LiteralPath $asmdefFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($asmdef.name) {
            $internalAssemblies[[string]$asmdef.name] = $true
        }
    }
}

$issues = New-Object System.Collections.Generic.List[object]

foreach ($packageFile in $packageFiles) {
    $packageDir = Split-Path -Parent $packageFile
    $packageJson = Get-Content -LiteralPath $packageFile -Raw -Encoding UTF8 | ConvertFrom-Json

    foreach ($asmdefFile in @(git ls-files --cached --others --exclude-standard -- "$packageDir/*.asmdef" "$packageDir/**/*.asmdef")) {
        $asmdef = Get-Content -LiteralPath $asmdefFile -Raw -Encoding UTF8 | ConvertFrom-Json

        if (@($asmdef.defineConstraints).Count -gt 0) {
            continue
        }

        foreach ($reference in @($asmdef.references)) {
            if (-not $reference) {
                continue
            }

            if ($internalAssemblies.ContainsKey($reference) -or $knownUnityAssemblies -contains $reference) {
                continue
            }

            $issues.Add([pscustomobject]@{
                Package = [string]$packageJson.name
                Assembly = [string]$asmdef.name
                Reference = [string]$reference
            })
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object Package, Assembly, Reference | Format-Table -AutoSize
    throw 'Unknown hard external asmdef references found. Move optional integrations behind defineConstraints or declare a reviewed package dependency.'
}

Write-Host "External assembly reference validation passed for $($packageFiles.Count) packages."
