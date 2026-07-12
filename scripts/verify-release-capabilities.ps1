[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    else {
        $PSScriptRoot
    }

    $RepositoryRoot = Split-Path -Parent $scriptRoot
}

$inputRoot = Join-Path $RepositoryRoot 'com.zerogamestudio.zeroengine.input'
$requiredPaths = @(
    'package.json',
    'Runtime/ZeroEngine.Input.asmdef',
    'Runtime/InputSystem/InputManager.cs',
    'Runtime/InputSystem/InputActionCatalog.cs',
    'Runtime/InputSystem/InputActionKey.cs',
    'Runtime/InputSystem/InputActionLookup.cs',
    'Runtime/InputSystem/InputBindingDisplayService.cs',
    'Runtime/InputSystem/InputBindingOverrideService.cs',
    'Runtime/InputSystem/InputBindingConflictValidator.cs',
    'Runtime/InputSystem/InputControlSchemeResolver.cs',
    'Runtime/InputSystem/InputRebindService.cs',
    'Runtime/InputSystem/InputSettingsModelBuilder.cs',
    'Tests/Editor/ZeroEngine.Input.Tests.Editor.asmdef',
    'Tests/Editor/InputActionCatalogValidatorTests.cs',
    'Tests/Editor/InputActionLookupTests.cs',
    'Tests/Editor/InputActionTestAssetFactory.cs',
    'Tests/Editor/InputBindingConflictValidatorTests.cs',
    'Tests/Editor/InputBindingDisplayServiceTests.cs',
    'Tests/Editor/InputBindingOverrideServiceTests.cs',
    'Tests/Editor/InputControlSchemeResolverTests.cs',
    'Tests/Editor/InputRebindServiceTests.cs',
    'Tests/Editor/InputSettingsModelBuilderTests.cs'
)

$missing = foreach ($relativePath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $inputRoot $relativePath))) {
        $relativePath
    }
}

if ($missing) {
    throw "ZeroEngine.Input capability regression. Missing: $($missing -join ', ')"
}

$missingMeta = foreach ($relativePath in $requiredPaths) {
    $isUnitySource = $relativePath.EndsWith('.cs') -or $relativePath.EndsWith('.asmdef')
    $metaPath = Join-Path $inputRoot ($relativePath + '.meta')
    if ($isUnitySource -and -not (Test-Path -LiteralPath $metaPath)) {
        $relativePath + '.meta'
    }
}

if ($missingMeta) {
    throw "ZeroEngine.Input Unity metadata regression. Missing: $($missingMeta -join ', ')"
}

$package = Get-Content -LiteralPath (Join-Path $inputRoot 'package.json') -Raw | ConvertFrom-Json
if ($package.description -notmatch 'rebind' -or $package.description -notmatch 'conflict') {
    throw 'ZeroEngine.Input package description does not declare the graduated kernel.'
}

$workflowPath = Join-Path $RepositoryRoot '.github/workflows/tests.yml'
$workflow = Get-Content -LiteralPath $workflowPath -Raw
$testablesMatch = [regex]::Match($workflow, '(?s)"testables"\s*:\s*\[(?<body>.*?)\]')
if (-not $testablesMatch.Success) {
    throw 'Unity CI workflow does not contain a testables manifest.'
}

$actualTestables = @(
    [regex]::Matches(
        $testablesMatch.Groups['body'].Value,
        '"(?<name>com\.zerogamestudio\.zeroengine[^"]*)"') |
        ForEach-Object { $_.Groups['name'].Value }
)
$expectedTestables = @(
    Get-ChildItem -LiteralPath $RepositoryRoot -Directory -Filter 'com.zerogamestudio.zeroengine*' |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Tests') } |
        Select-Object -ExpandProperty Name
)
$missingTestables = @($expectedTestables | Where-Object { $_ -notin $actualTestables })
$unexpectedTestables = @($actualTestables | Where-Object { $_ -notin $expectedTestables })
$duplicateTestables = @($actualTestables | Group-Object | Where-Object { $_.Count -ne 1 })

if ($missingTestables -or $unexpectedTestables -or $duplicateTestables) {
    $details = @(
        $missingTestables | ForEach-Object { "missing $_" }
        $unexpectedTestables | ForEach-Object { "unexpected $_" }
        $duplicateTestables | ForEach-Object { "duplicate $($_.Name) x$($_.Count)" }
    )
    throw "Unity CI testables regression: $($details -join ', ')"
}

$testSourceCount = @(Get-ChildItem -LiteralPath (Join-Path $inputRoot 'Tests/Editor') -Filter '*Tests.cs' -File).Count
Write-Host "ZeroEngine.Input capability guard passed with $testSourceCount test source files and $($actualTestables.Count) CI testable packages."
