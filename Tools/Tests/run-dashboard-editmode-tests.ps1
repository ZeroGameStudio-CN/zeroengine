[CmdletBinding()]
param(
    [string]$UnityPath = 'D:\unity\editors\Unity 2022.3.62f3\Editor\Unity.exe',
    [string]$ResultsDir = (Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-dashboard-results-' + [guid]::NewGuid().ToString('N'))),
    [ValidateSet('dashboard-only', 'dashboard-with-modules', 'modules-only')]
    [string[]]$Lanes = @('dashboard-only', 'dashboard-with-modules', 'modules-only'),
    [string]$TestFilter,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity Editor not found: $UnityPath"
}

New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
$laneDefinitions = @(
    @{ Name = 'dashboard-only'; Packages = @('com.zerogamestudio.zeroengine.editor-ui', 'com.zerogamestudio.zeroengine.dashboard') },
    @{ Name = 'dashboard-with-modules'; Packages = @('com.zerogamestudio.zeroengine.editor-ui', 'com.zerogamestudio.zeroengine.dashboard', 'com.zerogamestudio.zeroengine.project-atlas', 'com.zerogamestudio.zeroengine.core', 'com.zerogamestudio.zeroengine.ui', 'com.zerogamestudio.analytics') },
    @{ Name = 'modules-only'; Packages = @(Get-ChildItem -LiteralPath $repositoryRoot -Directory |
        Where-Object {
            ($_.Name -like 'com.zerogamestudio.zeroengine.*' -and $_.Name -ne 'com.zerogamestudio.zeroengine.dashboard') -or
            $_.Name -eq 'com.zerogamestudio.analytics'
        } |
        Sort-Object Name |
        Select-Object -ExpandProperty Name) }
)
$selectedLaneDefinitions = @($laneDefinitions | Where-Object { $Lanes -contains $_.Name })

foreach ($lane in $selectedLaneDefinitions) {
    $projectRoot = Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-dashboard-' + $lane.Name + '-' + [guid]::NewGuid().ToString('N'))
    $assetsPath = Join-Path $projectRoot 'Assets'
    $packagesPath = Join-Path $projectRoot 'Packages'
    $settingsPath = Join-Path $projectRoot 'ProjectSettings'
    New-Item -ItemType Directory -Path $assetsPath, $packagesPath, $settingsPath -Force | Out-Null

    foreach ($packageName in $lane.Packages) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $packageName) -Destination (Join-Path $packagesPath $packageName) -Recurse
    }

    $dependencies = [ordered]@{
        'com.unity.test-framework' = '1.3.9'
        'com.unity.modules.physics' = '1.0.0'
        'com.unity.modules.physics2d' = '1.0.0'
        'com.unity.modules.ai' = '1.0.0'
        'com.unity.modules.animation' = '1.0.0'
        'com.unity.modules.audio' = '1.0.0'
        'com.unity.modules.imgui' = '1.0.0'
        'com.unity.modules.ui' = '1.0.0'
        'com.unity.modules.imageconversion' = '1.0.0'
        'com.unity.modules.unitywebrequest' = '1.0.0'
        'com.unity.modules.particlesystem' = '1.0.0'
        'com.unity.modules.tilemap' = '1.0.0'
    }
    foreach ($packageName in $lane.Packages) {
        $dependencies[$packageName] = 'file:' + $packageName
    }
    if ($lane.Name -eq 'dashboard-with-modules') {
        $fixturePath = (Resolve-Path (Join-Path $repositoryRoot 'Tools\Tests\Fixtures\com.zerogamestudio.dashboard-removal-fixture')).Path.Replace('\', '/')
        $dependencies['com.zerogamestudio.dashboard-removal-fixture'] = 'file:' + $fixturePath
    }

    $manifest = [ordered]@{
        dependencies = $dependencies
        testables = @($lane.Packages)
    } | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $settingsPath 'ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8

    & (Join-Path $PSScriptRoot 'Test-ZeroEngineDashboardDescriptors.ps1') -RootPath $repositoryRoot -ProjectManifest (Join-Path $packagesPath 'manifest.json')

    if ($PrepareOnly) {
        Write-Output $projectRoot
        continue
    }

    $resultPath = Join-Path $ResultsDir ($lane.Name + '.xml')
    $logPath = Join-Path $ResultsDir ($lane.Name + '.log')
    $unityArguments = @(
        '-batchmode',
        '-nographics',
        '-projectPath', $projectRoot,
        '-runTests',
        '-testPlatform', 'EditMode',
        '-testResults', $resultPath,
        '-logFile', $logPath
    )
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $unityArguments += @('-testFilter', $TestFilter)
    }
    $previousRemovalTest = $env:ZEROENGINE_DASHBOARD_PACKAGE_REMOVAL_TEST
    $env:ZEROENGINE_DASHBOARD_PACKAGE_REMOVAL_TEST = if ($lane.Name -eq 'dashboard-with-modules') { '1' } else { '0' }
    $unityProcess = Start-Process `
        -FilePath $UnityPath `
        -ArgumentList $unityArguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    $unityExitCode = $unityProcess.ExitCode
    $env:ZEROENGINE_DASHBOARD_PACKAGE_REMOVAL_TEST = $previousRemovalTest

    if ($unityExitCode -ne 0) {
        throw "Unity failed for $($lane.Name). Exit=$unityExitCode Log=$logPath"
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "Unity produced no test result for $($lane.Name). Exit=$unityExitCode Log=$logPath"
    }
    [xml]$result = Get-Content -LiteralPath $resultPath -Raw
    $root = $result.'test-run'
    $acceptableResult = $root.result -eq 'Passed' -or $root.result -eq 'Skipped:Ignored'
    if (-not $acceptableResult -or [int]$root.total -le 0 -or [int]$root.passed -le 0 -or [int]$root.failed -ne 0) {
        throw "Dashboard lane $($lane.Name) failed. Result=$($root.result) Total=$($root.total) Passed=$($root.passed) Failed=$($root.failed) Log=$logPath"
    }
    Write-Host "PASS $($lane.Name) total=$($root.total) passed=$($root.passed)"

    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot)
    if ($resolvedProjectRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedProjectRoot).StartsWith('zeroengine-dashboard-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedProjectRoot -Recurse -Force
    }
}

if (-not $PrepareOnly) {
    Write-Host "PASS Dashboard EditMode matrix. Results: $ResultsDir"
}
