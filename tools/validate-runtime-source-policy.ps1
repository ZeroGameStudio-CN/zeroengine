[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    $rules = @(
        [pscustomobject]@{
            Name = "ResourcesLoad"
            Pattern = "Resources\.Load(?:Async|All)?\s*<"
            Description = "Runtime packages should not rely on Resources.Load as their primary dependency mechanism."
        },
        [pscustomobject]@{
            Name = "SceneFind"
            Pattern = "\b(?:FindObjectOfType|FindObjectsOfType)\s*<|GameObject\.Find(?:GameObjectWithTag)?\s*\("
            Description = "Runtime packages should use explicit references, registries, or providers instead of scene searches."
        },
        [pscustomobject]@{
            Name = "AssetsPath"
            Pattern = "`"Assets/"
            Description = "Runtime packages should not encode consumer project Assets paths."
        }
    )

    $knownDebtLimits = @{}

    $sourceFiles = @(
        git ls-files --cached --others --exclude-standard -- "com.zerogamestudio.*" |
            Where-Object {
                $normalized = $_.Replace("\", "/")
                $normalized.EndsWith(".cs", [System.StringComparison]::OrdinalIgnoreCase) -and
                $normalized.Contains("/Runtime/")
            } |
            Sort-Object -Unique
    )

    $findings = New-Object System.Collections.Generic.List[object]
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($file in $sourceFiles) {
        $fileText = Get-Content -LiteralPath $file -Raw -Encoding UTF8
        $normalizedPath = $file.Replace("\", "/")
        if ($fileText -match "\busing\s+UnityEditor\s*;") {
            $failures.Add("$normalizedPath UnityEditorImport: Runtime source must not import UnityEditor directly.")
        }

        if ($fileText -match "\bUnityEditor\." -and $fileText -notmatch "#if\s+UNITY_EDITOR") {
            $failures.Add("$normalizedPath UnityEditorGuard: Runtime UnityEditor calls must be guarded by UNITY_EDITOR.")
        }

        $lines = @(Get-Content -LiteralPath $file)
        for ($index = 0; $index -lt $lines.Count; $index++) {
            foreach ($rule in $rules) {
                if ($lines[$index] -match $rule.Pattern) {
                    $findings.Add([pscustomobject]@{
                        Key = "$normalizedPath|$($rule.Name)"
                        Path = $normalizedPath
                        Line = $index + 1
                        Rule = $rule.Name
                        Text = $lines[$index].Trim()
                    })
                }
            }
        }
    }

    foreach ($group in ($findings | Group-Object -Property Key)) {
        $limit = 0
        if ($knownDebtLimits.ContainsKey($group.Name)) {
            $limit = $knownDebtLimits[$group.Name]
        }

        if ($group.Count -gt $limit) {
            $sample = $group.Group | Select-Object -First 1
            $failures.Add("$($sample.Path) $($sample.Rule): found $($group.Count), allowed $limit")
        }
    }

    if ($failures.Count -gt 0) {
        Write-Error ("Runtime source policy failed:`n" + ($failures -join "`n"))
        exit 1
    }

    if ($findings.Count -gt 0) {
        Write-Warning "Known runtime project-assumption debt remains. Reduce tools/validate-runtime-source-policy.ps1 limits as packages are cleaned."
        foreach ($finding in ($findings | Sort-Object Path, Rule, Line)) {
            Write-Warning "$($finding.Path):$($finding.Line) [$($finding.Rule)] $($finding.Text)"
        }
    }

    Write-Host "Runtime source policy passed: $($sourceFiles.Count) Runtime C# files, $($findings.Count) known debt occurrences."
}
finally {
    Pop-Location
}
