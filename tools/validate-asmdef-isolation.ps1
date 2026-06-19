Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-JsonArrayProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }

    return @($property.Value | ForEach-Object { $_ })
}

$asmdefFiles = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio*.asmdef' 'com.zerogamestudio*/**/*.asmdef' |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

if ($asmdefFiles.Count -eq 0) {
    throw 'No ZeroEngine asmdef files found.'
}

$issues = New-Object System.Collections.Generic.List[string]
$nameToFiles = @{}

foreach ($file in $asmdefFiles) {
    $asmdef = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    $name = [string]$asmdef.name
    if ([string]::IsNullOrWhiteSpace($name)) {
        $issues.Add("${file}: asmdef is missing a non-empty name.")
        continue
    }

    if (-not $nameToFiles.ContainsKey($name)) {
        $nameToFiles[$name] = New-Object System.Collections.Generic.List[string]
    }

    $nameToFiles[$name].Add($file)
}

foreach ($entry in $nameToFiles.GetEnumerator()) {
    if ($entry.Value.Count -gt 1) {
        $issues.Add("Duplicate asmdef name '$($entry.Key)': $($entry.Value -join ', ')")
    }
}

foreach ($file in $asmdefFiles) {
    $normalizedPath = $file -replace '\\', '/'
    $asmdef = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    $references = @(Get-JsonArrayProperty $asmdef 'references')
    $includePlatforms = @(Get-JsonArrayProperty $asmdef 'includePlatforms')
    $isTestAssembly = $normalizedPath -match '/Tests/'
    $isEditorAssembly = $normalizedPath -match '/Editor/' -and -not $isTestAssembly
    $isRuntimeAssembly = $normalizedPath -match '/Runtime/' -and -not $isTestAssembly

    foreach ($reference in $references) {
        if ($reference -like 'GUID:*') {
            $issues.Add("${file}: asmdef references must use assembly names, not GUID references '$reference'.")
            continue
        }

        if ($reference -match '^(ZeroEngine|ZGS)(\.|$)' -and -not $nameToFiles.ContainsKey([string]$reference)) {
            $issues.Add("${file}: ZeroEngine/ZGS asmdef reference '$reference' does not resolve to a package asmdef.")
        }
    }

    if ($isEditorAssembly -and ($includePlatforms.Count -ne 1 -or $includePlatforms[0] -ne 'Editor')) {
        $issues.Add("${file}: production Editor asmdef must include only the Editor platform.")
    }

    if ($isRuntimeAssembly -and $includePlatforms.Count -eq 1 -and $includePlatforms[0] -eq 'Editor') {
        $issues.Add("${file}: production Runtime asmdef must not be Editor-only.")
    }

    if (-not $isTestAssembly) {
        $testReferences = @($references | Where-Object {
                $_ -eq 'UnityEditor.TestRunner' -or
                $_ -eq 'UnityEngine.TestRunner' -or
                $_ -match '(^|\.)(Tests?|TestRunner)(\.|$)'
            })
        foreach ($testReference in $testReferences) {
            $issues.Add("${file}: production asmdef must not reference test assembly '$testReference'.")
        }
    }

    if ($isRuntimeAssembly) {
        $editorReferences = @($references | Where-Object { $_ -match '(^|\.)(Editor)$' })
        foreach ($editorReference in $editorReferences) {
            $issues.Add("${file}: production Runtime asmdef must not reference Editor assembly '$editorReference'.")
        }
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Asmdef isolation validation failed.'
}

Write-Host "Asmdef isolation validation passed: $($asmdefFiles.Count) asmdef files."
