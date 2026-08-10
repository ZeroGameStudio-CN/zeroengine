[CmdletBinding()]
param(
    [string]$UnityPath = 'D:\unity\editors\Unity 2022.3.62f3\Editor\Unity.exe',
    [string]$ResultsDir = (Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-editor-ui-results-' + [guid]::NewGuid().ToString('N'))),
    [ValidateSet('editor-ui', 'analytics', 'config-pipeline', 'formula', 'tce', 'legacy-all')]
    [string[]]$Lanes = @('editor-ui', 'analytics', 'config-pipeline', 'formula', 'tce', 'legacy-all'),
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity Editor not found: $UnityPath"
}

New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
$editorUiPackage = 'com.zerogamestudio.zeroengine.editor-ui'
$legacyPackages = @(Get-ChildItem -LiteralPath $repositoryRoot -Directory |
    Where-Object {
        ($_.Name -eq 'com.zerogamestudio.zeroengine' -or $_.Name -like 'com.zerogamestudio.zeroengine.*') -and
        $_.Name -ne 'com.zerogamestudio.zeroengine.modsystem'
    } |
    Sort-Object Name |
    Select-Object -ExpandProperty Name)
$legacyPackages = @(@('com.zerogamestudio.analytics') + $legacyPackages | Select-Object -Unique)

$laneDefinitions = @(
    @{ Name = 'editor-ui'; Packages = @($editorUiPackage) },
    @{ Name = 'analytics'; Packages = @($editorUiPackage, 'com.zerogamestudio.analytics') },
    @{ Name = 'config-pipeline'; Packages = @($editorUiPackage, 'com.zerogamestudio.zeroengine.config-pipeline') },
    @{ Name = 'formula'; Packages = @($editorUiPackage, 'com.zerogamestudio.zeroengine.formula') },
    @{ Name = 'tce'; Packages = @($editorUiPackage, 'com.zerogamestudio.zeroengine.tce') },
    @{ Name = 'legacy-all'; Packages = $legacyPackages }
)

foreach ($lane in @($laneDefinitions | Where-Object { $Lanes -contains $_.Name })) {
    $projectRoot = Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-editor-ui-' + $lane.Name + '-' + [guid]::NewGuid().ToString('N'))
    $assetsPath = Join-Path $projectRoot 'Assets'
    $packagesPath = Join-Path $projectRoot 'Packages'
    $settingsPath = Join-Path $projectRoot 'ProjectSettings'
    New-Item -ItemType Directory -Path $assetsPath, $packagesPath, $settingsPath -Force | Out-Null

    foreach ($packageName in $lane.Packages) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $packageName) -Destination (Join-Path $packagesPath $packageName) -Recurse
    }

    $dependencies = [ordered]@{
        'com.unity.test-framework' = '1.3.9'
        'com.unity.inputsystem' = '1.17.0'
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

    $manifest = [ordered]@{
        dependencies = $dependencies
        testables = @($editorUiPackage)
    } | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $settingsPath 'ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8

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
        '-assemblyNames', 'ZeroEngine.EditorUI.Tests.Editor',
        '-testResults', $resultPath,
        '-logFile', $logPath
    )
    $unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -Wait -PassThru -WindowStyle Hidden
    if ($unityProcess.ExitCode -ne 0) {
        throw "Unity failed for $($lane.Name). Exit=$($unityProcess.ExitCode) Log=$logPath"
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "Unity produced no result for $($lane.Name). Log=$logPath"
    }

    [xml]$result = Get-Content -LiteralPath $resultPath -Raw
    $root = $result.'test-run'
    if ($root.result -ne 'Passed' -or [int]$root.failed -ne 0) {
        throw "Editor UI lane $($lane.Name) failed. Result=$($root.result) Passed=$($root.passed) Failed=$($root.failed) Log=$logPath"
    }
}

if (-not $PrepareOnly) {
    Write-Host "PASS Editor UI lanes=$($Lanes -join ',') Results=$ResultsDir"
}
