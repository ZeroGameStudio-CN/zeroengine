param(
    [string]$UnityPath = "D:/unity/editors/Unity 2022.3.62f3/Editor/Unity.exe",
    [string]$ResultsPath = "$env:TEMP/ZeroEngine-DataToolkit-EditMode.xml",
    [string]$LogPath = "$env:TEMP/ZeroEngine-DataToolkit-EditMode.log",
    [string]$ProjectPath = "$env:TEMP/ZeroEngine-DataToolkitTestProject"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$packageName = "com.zerogamestudio.zeroengine.data-toolkit"
$sourcePackagePath = Join-Path $repoRoot $packageName
$projectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$projectPackagesPath = Join-Path $projectPath "Packages"
$projectSettingsPath = Join-Path $projectPath "ProjectSettings"
$projectPackagePath = Join-Path $projectPackagesPath $packageName
$resolvedTempPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')

function Invoke-UnityBatch {
    param(
        [string[]]$Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $UnityPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    return $process.ExitCode
}

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity editor was not found: $UnityPath"
}

if (-not (Test-Path -LiteralPath $sourcePackagePath)) {
    throw "Data Toolkit package was not found: $sourcePackagePath"
}

if (-not $projectPath.StartsWith($resolvedTempPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to recreate test project outside TEMP: $projectPath"
}

if (Test-Path -LiteralPath $projectPath) {
    Remove-Item -LiteralPath $projectPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $projectPath "Assets") | Out-Null
New-Item -ItemType Directory -Force -Path $projectPackagesPath | Out-Null
New-Item -ItemType Directory -Force -Path $projectSettingsPath | Out-Null
Copy-Item -LiteralPath $sourcePackagePath -Destination $projectPackagePath -Recurse

@"
{
  "dependencies": {
    "com.unity.test-framework": "1.3.9",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "$packageName": "file:$packageName"
  },
  "testables": [
    "$packageName"
  ]
}
"@ | Set-Content -LiteralPath (Join-Path $projectPackagesPath "manifest.json") -Encoding UTF8

@"
m_EditorVersion: 2022.3.62f3
"@ | Set-Content -LiteralPath (Join-Path $projectSettingsPath "ProjectVersion.txt") -Encoding UTF8

$importLogPath = [System.IO.Path]::ChangeExtension($LogPath, ".import.log")

foreach ($outputPath in @($ResultsPath, $LogPath, $importLogPath)) {
    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Force
    }
}

$importExitCode = Invoke-UnityBatch @(
    "-batchmode",
    "-nographics",
    "-projectPath", $projectPath,
    "-logFile", $importLogPath,
    "-quit"
)

if ($importExitCode -ne 0) {
    exit $importExitCode
}

$testExitCode = Invoke-UnityBatch @(
    "-batchmode",
    "-projectPath", $projectPath,
    "-runTests",
    "-testPlatform", "EditMode",
    "-assemblyNames", "ZGS.DataToolkit.Editor.Tests",
    "-testResults", $ResultsPath,
    "-logFile", $LogPath
)

if ($testExitCode -eq 0 -and -not (Test-Path -LiteralPath $ResultsPath)) {
    Write-Error "Unity returned success but did not write test results: $ResultsPath"
    exit 1
}

exit $testExitCode
