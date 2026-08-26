[CmdletBinding()]
param(
    [string]$UnityPath = 'D:\unity\editors\Unity 2022.3.62f3\Editor\Unity.exe',
    [string]$ResultsDir = (Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-project-atlas-results-' + [guid]::NewGuid().ToString('N'))),
    [string]$TestFilter = 'ZeroEngine.ProjectAtlas.Tests',
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity Editor not found: $UnityPath"
}

$projectRoot = Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-project-atlas-' + [guid]::NewGuid().ToString('N'))
$packagesPath = Join-Path $projectRoot 'Packages'
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Assets'), $packagesPath, (Join-Path $projectRoot 'ProjectSettings') -Force | Out-Null

foreach ($packageName in @('com.zerogamestudio.zeroengine.editor-ui', 'com.zerogamestudio.zeroengine.project-atlas')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $packageName) -Destination (Join-Path $packagesPath $packageName) -Recurse
}

$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.test-framework' = '1.3.9'
        'com.unity.modules.imgui' = '1.0.0'
        'com.zerogamestudio.zeroengine.editor-ui' = 'file:com.zerogamestudio.zeroengine.editor-ui'
        'com.zerogamestudio.zeroengine.project-atlas' = 'file:com.zerogamestudio.zeroengine.project-atlas'
    }
    testables = @('com.zerogamestudio.zeroengine.project-atlas')
} | ConvertTo-Json -Depth 8
Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding UTF8
Set-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8

if ($PrepareOnly) {
    Write-Output $projectRoot
    return
}

New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
$resultPath = Join-Path $ResultsDir 'project-atlas.xml'
$logPath = Join-Path $ResultsDir 'project-atlas.log'
$arguments = @(
    '-batchmode', '-nographics',
    '-projectPath', $projectRoot,
    '-runTests', '-testPlatform', 'EditMode',
    '-testResults', $resultPath,
    '-logFile', $logPath,
    '-testFilter', $TestFilter
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Unity failed. Exit=$($process.ExitCode) Log=$logPath"
}
if (-not (Test-Path -LiteralPath $resultPath)) {
    throw "Unity produced no result. Log=$logPath"
}
[xml]$result = Get-Content -LiteralPath $resultPath -Raw
$run = $result.'test-run'
if ([int]$run.total -le 0 -or [int]$run.failed -ne 0) {
    throw "Project Atlas tests failed. Result=$($run.result) Total=$($run.total) Failed=$($run.failed) Log=$logPath"
}
if (Select-String -LiteralPath $logPath -Pattern 'error CS\d+:' -Quiet) {
    throw "Project Atlas Unity log contains compile errors: $logPath"
}
Write-Host "PASS Project Atlas total=$($run.total) passed=$($run.passed) results=$ResultsDir"

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot)
if ($resolvedProjectRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
    (Split-Path -Leaf $resolvedProjectRoot).StartsWith('zeroengine-project-atlas-', [StringComparison]::Ordinal)) {
    Remove-Item -LiteralPath $resolvedProjectRoot -Recurse -Force
}
