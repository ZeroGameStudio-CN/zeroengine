#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$package = Join-Path $repo 'com.zerogamestudio.zeroengine.narrative/Runtime/Achievement'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('zeroengine-steam-platforms-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$runner = Join-Path $scratch 'case.ps1'
@'
#Requires -Version 7.0
param([string]$Package, [string]$Scratch, [string]$Defines, [switch]$WithSteam)
$ErrorActionPreference = 'Stop'
$unity = Join-Path $Scratch 'unity.cs'
@"
namespace UnityEngine {
 public static class Debug { public static int Warnings; public static void LogWarning(object value) { Warnings++; } }
 public static class Mathf { public static float Clamp01(float value) { return System.Math.Max(0f, System.Math.Min(1f, value)); } }
}
"@ | Set-Content $unity -Encoding utf8
$paths = @((Join-Path $Package 'Providers/SteamAchievementProvider.cs'), (Join-Path $Package 'IAchievementProvider.cs'), $unity)
if ($WithSteam) {
 $steam = Join-Path $Scratch 'steam.cs'
 @"
public static class SteamManager { public static bool Initialized = true; }
namespace Steamworks {
 public static class SteamUserStats {
  public static int Requests, Unlocked, Stored; public static uint Current, Maximum;
  public static bool RequestCurrentStats() { Requests++; return true; }
  public static bool GetAchievement(string id, out bool achieved) { achieved = false; return true; }
  public static bool SetAchievement(string id) { Unlocked++; return true; }
  public static bool StoreStats() { Stored++; return true; }
  public static bool IndicateAchievementProgress(string id, uint current, uint maximum) { Current = current; Maximum = maximum; return true; }
 }
}
"@ | Set-Content $steam -Encoding utf8
 $paths += $steam
}
Add-Type -Path $paths -CompilerOptions "/define:$Defines"
$provider = [ZeroEngine.Achievement.Providers.SteamAchievementProvider]::new()
if ($provider.Initialize() -ne [bool]$WithSteam) { throw 'Unexpected platform initialization result' }
$provider.Unlock('fixture-achievement')
$provider.SetProgress('fixture-achievement', [single]0.75)
if ($WithSteam) {
 if ([Steamworks.SteamUserStats]::Requests -ne 1 -or [Steamworks.SteamUserStats]::Unlocked -ne 1 -or [Steamworks.SteamUserStats]::Stored -ne 1 -or [Steamworks.SteamUserStats]::Current -ne 75 -or [Steamworks.SteamUserStats]::Maximum -ne 100) { throw 'Desktop Steam calls changed' }
} elseif ([UnityEngine.Debug]::Warnings -ne 1) { throw 'Unsupported platforms must report an unavailable provider' }
'@ | Set-Content $runner -Encoding utf8
$cases = @(
    @{ Defines = 'STEAMWORKS_NET,UNITY_ANDROID,UNITY_EDITOR'; Steam = $false },
    @{ Defines = 'STEAMWORKS_NET,UNITY_ANDROID'; Steam = $false },
    @{ Defines = 'STEAMWORKS_NET,UNITY_IOS'; Steam = $false },
    @{ Defines = 'STEAMWORKS_NET,UNITY_WEBGL'; Steam = $false },
    @{ Defines = 'STEAMWORKS_NET,UNITY_STANDALONE_WIN'; Steam = $true },
    @{ Defines = 'STEAMWORKS_NET,UNITY_STANDALONE_LINUX'; Steam = $true },
    @{ Defines = 'STEAMWORKS_NET,UNITY_STANDALONE_OSX'; Steam = $true },
    @{ Defines = 'UNITY_STANDALONE_WIN'; Steam = $false }
)
$pwsh = (Get-Process -Id $PID).Path
foreach ($case in $cases) {
    $arguments = @('-NoProfile', '-File', $runner, '-Package', $package, '-Scratch', $scratch, '-Defines', $case.Defines)
    if ($case.Steam) { $arguments += '-WithSteam' }
    & $pwsh @arguments
    if ($LASTEXITCODE) { throw "Platform compilation/behavior failed: $($case.Defines); fixtures retained at $scratch" }
}
if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($scratch)).TrimEnd('\', '/') -ne [IO.Path]::GetTempPath().TrimEnd('\', '/')) { throw 'Temporary cleanup target escaped its root' }
Remove-Item -LiteralPath $scratch -Recurse -Force
Write-Host "Narrative Steam platform regression: $($cases.Count) cases passed."
