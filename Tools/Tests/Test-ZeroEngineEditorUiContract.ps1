param(
    [string]$RepoRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)

$ErrorActionPreference = 'Stop'

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "Editor UI contract failed: $Message"
    }
}

function Read-Json([string]$Path) {
    Assert-Contract (Test-Path -LiteralPath $Path) "Missing JSON: $Path"
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function Get-CSharpMethodBodies([string]$Source, [string]$MethodName) {
    $escapedName = [regex]::Escape($MethodName)
    $matches = [regex]::Matches(
        $Source,
        "(?m)^\s*(?:public|private|protected|internal)\s+(?:override\s+)?(?:static\s+)?void\s+$escapedName\s*\(")
    $bodies = @()

    foreach ($match in $matches) {
        $start = $Source.IndexOf('{', $match.Index + $match.Length)
        if ($start -lt 0) { continue }

        $depth = 0
        $inString = $false
        $inChar = $false
        $inLineComment = $false
        $inBlockComment = $false
        $verbatim = $false
        $escaped = $false

        for ($index = $start; $index -lt $Source.Length; $index++) {
            $char = $Source[$index]
            $next = if ($index + 1 -lt $Source.Length) { $Source[$index + 1] } else { [char]0 }

            if ($inLineComment) {
                if ($char -eq "`n") { $inLineComment = $false }
                continue
            }
            if ($inBlockComment) {
                if ($char -eq '*' -and $next -eq '/') {
                    $inBlockComment = $false
                    $index++
                }
                continue
            }
            if ($inString) {
                if ($verbatim -and $char -eq '"' -and $next -eq '"') {
                    $index++
                    continue
                }
                if (-not $verbatim -and $char -eq '\' -and -not $escaped) {
                    $escaped = $true
                    continue
                }
                if ($char -eq '"' -and -not $escaped) { $inString = $false }
                $escaped = $false
                continue
            }
            if ($inChar) {
                if ($char -eq '\' -and -not $escaped) {
                    $escaped = $true
                    continue
                }
                if ($char -eq "'" -and -not $escaped) { $inChar = $false }
                $escaped = $false
                continue
            }
            if ($char -eq '/' -and $next -eq '/') {
                $inLineComment = $true
                $index++
                continue
            }
            if ($char -eq '/' -and $next -eq '*') {
                $inBlockComment = $true
                $index++
                continue
            }
            if ($char -eq '"') {
                $inString = $true
                $verbatim = $index -gt 0 -and $Source[$index - 1] -eq '@'
                continue
            }
            if ($char -eq "'") {
                $inChar = $true
                continue
            }
            if ($char -eq '{') { $depth++ }
            if ($char -eq '}') {
                $depth--
                if ($depth -eq 0) {
                    $bodies += $Source.Substring($start, $index - $start + 1)
                    break
                }
            }
        }
    }

    return $bodies
}

$expectedPackages = [ordered]@{
    'com.zerogamestudio.analytics' = '2.0.1'
    'com.zerogamestudio.zeroengine' = '2.1.1'
    'com.zerogamestudio.zeroengine.config-pipeline' = '2.0.2'
    'com.zerogamestudio.zeroengine.dashboard' = '4.7.0'
    'com.zerogamestudio.zeroengine.data-toolkit' = '2.1.1'
    'com.zerogamestudio.zeroengine.formula' = '0.6.0'
    'com.zerogamestudio.zeroengine.feedback' = '1.0.2'
    'com.zerogamestudio.zeroengine.modsystem' = '0.3.0'
    'com.zerogamestudio.zeroengine.project-atlas' = '1.1.5'
    'com.zerogamestudio.zeroengine.tce' = '0.2.1'
    'com.zerogamestudio.zeroengine.ui' = '2.2.1'
}

$editorUiRoot = Join-Path $RepoRoot 'com.zerogamestudio.zeroengine.editor-ui'
$editorUiPackage = Read-Json (Join-Path $editorUiRoot 'package.json')
Assert-Contract ($editorUiPackage.name -eq 'com.zerogamestudio.zeroengine.editor-ui') 'Unexpected editor-ui package name.'
Assert-Contract ($editorUiPackage.version -eq '1.5.0') 'editor-ui must be version 1.5.0.'
Assert-Contract ($editorUiPackage.unity -eq '2022.3') 'editor-ui must target Unity 2022.3.'
Assert-Contract ($null -eq $editorUiPackage.dependencies -or @($editorUiPackage.dependencies.psobject.Properties).Count -eq 0) 'editor-ui production package must not declare dependencies.'

$editorUiAsmdef = Read-Json (Join-Path $editorUiRoot 'Editor/ZeroEngine.EditorUI.Editor.asmdef')
Assert-Contract ($editorUiAsmdef.name -eq 'ZeroEngine.EditorUI.Editor') 'Unexpected editor-ui assembly name.'
Assert-Contract ($editorUiAsmdef.autoReferenced -eq $false) 'editor-ui assembly must set autoReferenced=false.'
Assert-Contract ($editorUiAsmdef.includePlatforms.Count -eq 1 -and $editorUiAsmdef.includePlatforms[0] -eq 'Editor') 'editor-ui assembly must be Editor-only.'

$productionSources = Get-ChildItem -LiteralPath (Join-Path $editorUiRoot 'Editor') -Filter '*.cs' -File -Recurse
$forbiddenProductionTokens = 'AssetDatabase|UnityEditor\.PackageManager|System\.IO|System\.Net|HttpClient|WebRequest|MenuItem|StyleSheet|\.uss|TypeCache|System\.Reflection'
foreach ($sourceFile in $productionSources) {
    $source = Get-Content -Raw -LiteralPath $sourceFile.FullName
    Assert-Contract (-not [regex]::IsMatch($source, $forbiddenProductionTokens)) "Forbidden production dependency in $($sourceFile.FullName)."
    if ($sourceFile.Name -ne 'EditorUiPalette.cs') {
        Assert-Contract (-not [regex]::IsMatch($source, '\b(?:new\s+)?Color(?:32)?\s*\(|\bColor(?:32)?\.')) "Color literal outside EditorUiPalette.cs: $($sourceFile.FullName)."
    }
}
$tokensSource = Get-Content -Raw -LiteralPath (Join-Path $editorUiRoot 'Editor/EditorUiTokens.cs')
Assert-Contract (-not [regex]::IsMatch($tokensSource, '\bColor(?:32)?\b')) 'EditorUiTokens.cs must contain sizes only.'

$asmdefs = [ordered]@{
    'com.zerogamestudio.analytics' = 'Editor/ZGS.Analytics.Editor.asmdef'
    'com.zerogamestudio.zeroengine' = 'Editor/ZeroEngine.Editor.asmdef'
    'com.zerogamestudio.zeroengine.config-pipeline' = 'Editor/ZGS.ConfigPipeline.Editor.asmdef'
    'com.zerogamestudio.zeroengine.dashboard' = 'Editor/ZeroEngine.Dashboard.Editor.asmdef'
    'com.zerogamestudio.zeroengine.data-toolkit' = 'Editor/ZGS.DataToolkit.Editor.asmdef'
    'com.zerogamestudio.zeroengine.formula' = 'Editor/ZeroEngine.Formula.Editor.asmdef'
    'com.zerogamestudio.zeroengine.feedback' = 'Editor/ZeroEngine.Feedback.Editor.asmdef'
    'com.zerogamestudio.zeroengine.modsystem' = 'Editor/Legacy/ZeroEngine.ModSystem.Editor.asmdef'
    'com.zerogamestudio.zeroengine.project-atlas' = 'Editor/ZeroEngine.ProjectAtlas.Editor.asmdef'
    'com.zerogamestudio.zeroengine.tce' = 'Editor/ZeroEngine.TCE.Editor.asmdef'
    'com.zerogamestudio.zeroengine.ui' = 'Editor/ZeroEngine.UI.Editor.asmdef'
}

$expectedEditorUiDependencies = @{
    'com.zerogamestudio.analytics' = '1.3.0'
    'com.zerogamestudio.zeroengine' = '1.3.0'
    'com.zerogamestudio.zeroengine.config-pipeline' = '1.3.0'
    'com.zerogamestudio.zeroengine.dashboard' = '1.5.0'
    'com.zerogamestudio.zeroengine.data-toolkit' = '1.4.0'
    'com.zerogamestudio.zeroengine.feedback' = '1.3.0'
    'com.zerogamestudio.zeroengine.formula' = '1.4.0'
    'com.zerogamestudio.zeroengine.modsystem' = '1.3.0'
    'com.zerogamestudio.zeroengine.project-atlas' = '1.5.0'
    'com.zerogamestudio.zeroengine.tce' = '1.3.0'
    'com.zerogamestudio.zeroengine.ui' = '1.3.0'
}

foreach ($packageName in $expectedPackages.Keys) {
    $packageRoot = Join-Path $RepoRoot $packageName
    $package = Read-Json (Join-Path $packageRoot 'package.json')
    Assert-Contract ($package.version -eq $expectedPackages[$packageName]) "$packageName version must be $($expectedPackages[$packageName])."
    $expectedEditorUiDependency = if ($expectedEditorUiDependencies.ContainsKey($packageName)) {
        $expectedEditorUiDependencies[$packageName]
    } else {
        '1.0.0'
    }
    Assert-Contract ($package.dependencies.'com.zerogamestudio.zeroengine.editor-ui' -eq $expectedEditorUiDependency) "$packageName must directly depend on editor-ui $expectedEditorUiDependency."

    $asmdef = Read-Json (Join-Path $packageRoot $asmdefs[$packageName])
    Assert-Contract ($asmdef.references -contains 'ZeroEngine.EditorUI.Editor') "$packageName Editor asmdef must explicitly reference editor-ui."
}

$projectAtlasPanelSource = Get-Content -Raw -LiteralPath (
    Join-Path $RepoRoot 'com.zerogamestudio.zeroengine.project-atlas/Editor/ProjectAtlasWorkspacePanel.cs')
Assert-Contract (
    [regex]::IsMatch($projectAtlasPanelSource, '(?s)DrawFeatureButtons.*?EditorUiGUILayout\.SelectionButton')) `
    'Project Atlas feature rows must use the standard centered selection button.'
Assert-Contract (-not $projectAtlasPanelSource.Contains('DrawFeatureRow')) `
    'Project Atlas feature buttons must not regress to split overlay labels.'
Assert-Contract (-not $projectAtlasPanelSource.Contains('GUIStyle.none')) `
    'Project Atlas feature rows must not regress to invisible click targets.'

$legacyPackage = Read-Json (Join-Path $RepoRoot 'com.zerogamestudio.zeroengine/package.json')
Assert-Contract ($null -eq $legacyPackage.dependencies.'com.zerogamestudio.zeroengine.dashboard') 'Legacy ZeroEngine must not depend on Dashboard.'

$coveragePath = Join-Path $editorUiRoot 'Tests/Editor/Fixtures/EditorUiWindowCoverage.json'
$coverage = Read-Json $coveragePath
Assert-Contract ($coverage.schemaVersion -eq 1) 'Coverage schemaVersion must be 1.'
Assert-Contract ($coverage.records.Count -eq 33) 'Coverage must contain 33 records.'
Assert-Contract (($coverage.records | Where-Object countsTowardModuleTotal).Count -eq 31) 'Coverage must count exactly 31 normal module types.'
Assert-Contract (($coverage.records.targetId | Sort-Object -Unique).Count -eq 33) 'Coverage targetId values must be unique.'
Assert-Contract (($coverage.records.typeName | Sort-Object -Unique).Count -eq 32) 'Coverage must contain 32 unique types including the Dashboard shell.'

$descriptorIds = @()
foreach ($packageDirectory in Get-ChildItem -LiteralPath $RepoRoot -Directory -Filter 'com.zerogamestudio.*') {
    $descriptorPath = Join-Path $packageDirectory.FullName 'Editor/ZeroEngineDashboardModule.json'
    if (-not (Test-Path -LiteralPath $descriptorPath)) { continue }
    $descriptor = Read-Json $descriptorPath
    foreach ($entry in $descriptor.entries) {
        if ($entry.kind -eq 'window') {
            $descriptorIds += "$($descriptor.moduleId)/$($entry.id)"
        }
    }
}
$coverageDescriptorIds = @($coverage.records | Where-Object { $null -ne $_.descriptorFullId } | ForEach-Object descriptorFullId)
Assert-Contract ($descriptorIds.Count -eq 31) 'Production descriptors must expose exactly 31 window routes.'
Assert-Contract ($coverageDescriptorIds.Count -eq 31) 'Coverage must bind exactly 31 descriptor routes.'
Assert-Contract ((Compare-Object ($descriptorIds | Sort-Object) ($coverageDescriptorIds | Sort-Object)).Count -eq 0) 'Coverage descriptor IDs must equal the production window descriptor set.'

foreach ($record in $coverage.records) {
    Assert-Contract ($record.migrationStatus -eq 'migrated') "$($record.targetId) is not migrated."
    Assert-Contract (@('imgui', 'ui-toolkit', 'conditional-odin') -contains $record.technology) "$($record.targetId) has an invalid technology."
    $sourcePath = Join-Path $RepoRoot $record.sourcePath
    Assert-Contract (Test-Path -LiteralPath $sourcePath) "$($record.targetId) source is missing."
    $source = Get-Content -Raw -LiteralPath $sourcePath
    $simpleTypeName = ($record.typeName -split '\.')[-1]
    Assert-Contract ([regex]::IsMatch($source, "\bclass\s+$([regex]::Escape($simpleTypeName))\b")) "$($record.targetId) type marker is missing."
    Assert-Contract ($source.Contains('EditorUiSurface')) "$($record.targetId) lacks EditorUiSurface."

    $methodNames = @($record.integrationMethod -split '\s*/\s*')
    $allBodies = @()
    foreach ($methodName in $methodNames) {
        $allBodies += @(Get-CSharpMethodBodies $source $methodName)
    }
    Assert-Contract ($allBodies.Count -gt 0) "$($record.targetId) integration method is missing."
    $joinedBodies = $allBodies -join "`n"
    Assert-Contract ([regex]::IsMatch($joinedBodies, 'EditorUiGUILayout|EditorUiElements|EditorUiStyles')) "$($record.targetId) integration method lacks a shared UI marker."
    Assert-Contract (-not [regex]::IsMatch($joinedBodies, '\b(?:new\s+)?Color(?:32)?\s*\(|\bColor(?:32)?\.')) "$($record.targetId) integration method contains an unapproved color token."
    Assert-Contract (-not [regex]::IsMatch($joinedBodies, 'new\s+GUIStyle\s*\(')) "$($record.targetId) integration method contains a top-level GUIStyle."

    foreach ($exception in @($record.exceptions)) {
        Assert-Contract (@('Color', 'Color32', 'GUIStyle') -contains $exception.token) "$($record.targetId) has an invalid exception token."
        Assert-Contract (@('domain-canvas', 'domain-chart') -contains $exception.scope) "$($record.targetId) has an invalid exception scope."
        Assert-Contract ($methodNames -notcontains $exception.method) "$($record.targetId) cannot exempt its integration method."
        Assert-Contract (-not [string]::IsNullOrWhiteSpace($exception.reason)) "$($record.targetId) exception needs a reason."
        $exceptionBodies = @(Get-CSharpMethodBodies $source $exception.method)
        Assert-Contract ($exceptionBodies.Count -gt 0) "$($record.targetId) exception method is missing."
        Assert-Contract (-not [regex]::IsMatch(($exceptionBodies -join "`n"), 'EditorUiGUILayout|EditorUiElements|Header|Card|Status')) "$($record.targetId) cannot exempt shared chrome."
    }
}

$globalSearch = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'com.zerogamestudio.zeroengine/Editor/GlobalSearchWindow.cs')
Assert-Contract (-not [regex]::IsMatch($globalSearch, 'ODIN_INSPECTOR|Sirenix|OdinEditorWindow')) 'GlobalSearchWindow must have one built-in implementation.'

Write-Host "ZeroEngine Editor UI contract passed: descriptors=31 coverage=33 modules=31"
