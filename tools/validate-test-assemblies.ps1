Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$testAsmdefs = @(git ls-files --cached --others --exclude-standard -- 'com.zerogamestudio.*/*Tests*.asmdef' 'com.zerogamestudio.*/**/*Tests*.asmdef' |
    Where-Object { Test-Path -LiteralPath $_ } |
    Sort-Object -Unique)

if ($testAsmdefs.Count -eq 0) {
    throw 'No package test asmdefs found.'
}

$issues = New-Object System.Collections.Generic.List[string]
$totalTestFiles = 0
$totalTestAttributes = 0

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

$packageDirectories = @(Get-ChildItem -Directory 'com.zerogamestudio*' | Sort-Object Name)
foreach ($packageDirectory in $packageDirectories) {
    $testsDirectory = Join-Path $packageDirectory.FullName 'Tests'
    if (-not (Test-Path -LiteralPath $testsDirectory)) {
        $issues.Add("$($packageDirectory.Name): package is missing a Tests directory.")
        continue
    }

    $testFiles = @(Get-ChildItem -LiteralPath $testsDirectory -Recurse -Filter '*.cs' -File)
    $totalTestFiles += $testFiles.Count
    if ($testFiles.Count -eq 0) {
        $issues.Add("$($packageDirectory.Name): Tests directory contains no C# test files.")
        continue
    }

    $testAttributeCount = 0
    foreach ($testFile in $testFiles) {
        $text = Get-Content -LiteralPath $testFile.FullName -Raw -Encoding UTF8
        $testAttributeCount += ([regex]::Matches($text, '\[(?:Test|UnityTest|TestCase|TestCaseSource)\b')).Count
    }

    if ($testAttributeCount -eq 0) {
        $issues.Add("$($packageDirectory.Name): Tests directory contains no NUnit or Unity test attributes.")
    }

    $totalTestAttributes += $testAttributeCount
}

foreach ($file in $testAsmdefs) {
    $json = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    $references = @(Get-JsonArrayProperty $json 'references')
    $includePlatforms = @(Get-JsonArrayProperty $json 'includePlatforms')
    $optionalUnityReferences = @(Get-JsonArrayProperty $json 'optionalUnityReferences')
    $normalizedPath = $file -replace '\\', '/'
    $hasModernTestRunnerReferences =
        $references -contains 'UnityEngine.TestRunner' -and
        $references -contains 'UnityEditor.TestRunner'
    $hasLegacyTestAssemblyMarker = $optionalUnityReferences -contains 'TestAssemblies'

    if (-not $hasModernTestRunnerReferences -and -not $hasLegacyTestAssemblyMarker) {
        $issues.Add("${file}: test asmdef must reference UnityEngine.TestRunner and UnityEditor.TestRunner, or declare optionalUnityReferences/TestAssemblies.")
    }

    if ($normalizedPath -match '/Tests/Editor/' -and -not ($includePlatforms -contains 'Editor')) {
        $issues.Add("${file}: Editor test asmdef must include the Editor platform.")
    }

    if ($normalizedPath -match '/Tests/Runtime/' -and ($includePlatforms -contains 'Editor')) {
        $issues.Add("${file}: Runtime test asmdef must not be Editor-only.")
    }
}

if ($issues.Count -gt 0) {
    $issues | Sort-Object
    throw 'Unity test assembly validation failed.'
}

Write-Host "Unity test assembly validation passed: $($packageDirectories.Count) packages, $($testAsmdefs.Count) asmdefs, $totalTestFiles C# test files, $totalTestAttributes test attributes."
