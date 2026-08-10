[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-editor-ui-visual-' + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectRoot = [IO.Path]::GetFullPath($OutputPath)
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

if (-not $projectRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Visual projects must be generated under the system temporary directory: $projectRoot"
}
if (Test-Path -LiteralPath $projectRoot) {
    throw "Refusing to overwrite an existing visual project: $projectRoot"
}

$packageNames = @('com.zerogamestudio.analytics') + @(Get-ChildItem -LiteralPath $repositoryRoot -Directory |
    Where-Object {
        ($_.Name -eq 'com.zerogamestudio.zeroengine' -or $_.Name -like 'com.zerogamestudio.zeroengine.*') -and
        $_.Name -ne 'com.zerogamestudio.zeroengine.modsystem'
    } |
    Sort-Object Name |
    Select-Object -ExpandProperty Name)

$dependencies = [ordered]@{
    'com.unity.test-framework' = '1.3.9'
    'com.unity.inputsystem' = '1.17.0'
    'com.unity.localization' = '1.4.0'
    'com.unity.addressables' = '1.21.0'
    'com.unity.textmeshpro' = '3.0.6'
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

foreach ($packageName in $packageNames) {
    $packagePath = Join-Path $repositoryRoot $packageName
    if (-not (Test-Path -LiteralPath (Join-Path $packagePath 'package.json'))) {
        throw "Missing package.json for visual package: $packageName"
    }
    $dependencies[$packageName] = 'file:' + $packagePath.Replace('\', '/')
}

$assetsPath = Join-Path $projectRoot 'Assets'
$packagesPath = Join-Path $projectRoot 'Packages'
$settingsPath = Join-Path $projectRoot 'ProjectSettings'
New-Item -ItemType Directory -Path $assetsPath, $packagesPath, $settingsPath -Force | Out-Null

$manifest = [ordered]@{
    dependencies = $dependencies
    testables = @('com.zerogamestudio.zeroengine.editor-ui')
} | ConvertTo-Json -Depth 8
Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding UTF8
Set-Content -LiteralPath (Join-Path $settingsPath 'ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.62f3' -Encoding UTF8

Write-Output $projectRoot
