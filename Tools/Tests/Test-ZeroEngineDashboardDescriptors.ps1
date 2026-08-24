[CmdletBinding()]
param(
    [string]$RootPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$ProjectManifest
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Find-PackageRoot {
    param([string]$DescriptorPath)
    $directory = Split-Path -Parent $DescriptorPath
    while ($directory -and $directory.StartsWith($RootPath, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath (Join-Path $directory 'package.json')) {
            return $directory
        }
        $parent = Split-Path -Parent $directory
        if ($parent -eq $directory) {
            break
        }
        $directory = $parent
    }
    return $null
}

$descriptorPaths = @(Get-ChildItem -LiteralPath $RootPath -Recurse -File -Filter 'ZeroEngineDashboardModule.json' |
    Where-Object { $_.FullName -notmatch '[\\/](Library|Temp|Logs|TestProject)[\\/]' } |
    Where-Object { $_.FullName -notmatch '[\\/]Tools[\\/]Tests[\\/]Fixtures[\\/]' } |
    Sort-Object FullName |
    Select-Object -ExpandProperty FullName)

Assert-Condition ($descriptorPaths.Count -gt 0) 'No Dashboard descriptors were found.'

$dataToolkitDescriptorPath = Join-Path $RootPath 'com.zerogamestudio.zeroengine.data-toolkit\Editor\ZeroEngineDashboardModule.json'
Assert-Condition (Test-Path -LiteralPath $dataToolkitDescriptorPath) 'Data Toolkit Dashboard host descriptor is required.'
$dataToolkitDescriptor = Get-Content -LiteralPath $dataToolkitDescriptorPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Condition (@($dataToolkitDescriptor.entries).Count -eq 0) 'Data Toolkit Dashboard host descriptor must not invent generic entries.'

$allowedCategories = @('authoring', 'data-localization', 'assets-build', 'diagnostics', 'test-release', 'system-setup')
$allowedKinds = @('window', 'command')
$allowedSafety = @('navigation', 'read-only', 'project-write', 'destructive')
$allowedAvailability = @('always', 'edit-mode', 'play-mode')
$allowedScopes = @('universal', 'project')
$allowedVisibility = @('primary', 'advanced', 'maintenance')
$allowedContentTypes = @('action', 'reference')
$stableModuleIdPattern = '^[a-z0-9]+(?:[.-][a-z0-9]+)*$'
$stableActionIdPattern = '^[a-z0-9]+(?:-[a-z0-9]+)*$'
$hanPattern = '[\u3400-\u9fff]'
$actionBindings = @{}

Assert-Condition ('project.target' -match $stableModuleIdPattern) 'Stable module ID validation rejected a valid fixture.'
Assert-Condition ('Project Target' -notmatch $stableModuleIdPattern) 'Stable module ID validation accepted an invalid fixture.'

foreach ($descriptorPath in $descriptorPaths) {
    Assert-Condition (Test-Path -LiteralPath ($descriptorPath + '.meta')) "Missing .meta for $descriptorPath"
    $descriptor = Get-Content -LiteralPath $descriptorPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $packageRoot = Find-PackageRoot $descriptorPath
    Assert-Condition ($null -ne $packageRoot) "Descriptor is not inside a package: $descriptorPath"
    $package = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json

    Assert-Condition ($descriptor.schemaVersion -eq 2) "First-party descriptor schemaVersion must be 2: $descriptorPath"
    Assert-Condition ($descriptor.moduleId -match $stableModuleIdPattern) "Invalid moduleId '$($descriptor.moduleId)' in $descriptorPath"
    Assert-Condition ($descriptor.moduleId -eq $package.name) "moduleId must equal package name in $descriptorPath"
    Assert-Condition ($allowedScopes -contains $descriptor.scope) "Invalid scope '$($descriptor.scope)' in $descriptorPath"
    if ($descriptor.scope -eq 'project') {
        Assert-Condition ($descriptor.projectId -match $stableModuleIdPattern) "Project scope needs a stable projectId: $descriptorPath"
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($descriptor.projectDisplayName)) "Project scope needs projectDisplayName: $descriptorPath"
    } else {
        Assert-Condition ([string]::IsNullOrWhiteSpace($descriptor.projectId)) "Universal scope must not declare projectId: $descriptorPath"
        Assert-Condition ([string]::IsNullOrWhiteSpace($descriptor.projectDisplayName)) "Universal scope must not declare projectDisplayName: $descriptorPath"
    }
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($descriptor.displayName)) "displayName is required: $descriptorPath"
    Assert-Condition ($descriptor.description -match $hanPattern) "Chinese module description is required: $descriptorPath"
    Assert-Condition ($null -ne $descriptor.entries) "entries is required: $descriptorPath"

    $ids = @{}
    $sourceText = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter '*.cs' |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"

    foreach ($entry in @($descriptor.entries)) {
        Assert-Condition ($entry.id -match $stableActionIdPattern) "Invalid entry id '$($entry.id)' in $descriptorPath"
        Assert-Condition (-not $ids.ContainsKey($entry.id)) "Duplicate entry id '$($entry.id)' in $descriptorPath"
        $ids[$entry.id] = $true
        Assert-Condition ($entry.displayName -match $hanPattern) "Chinese entry displayName is required for '$($entry.id)' in $descriptorPath"
        Assert-Condition ($entry.description -match $hanPattern) "Chinese entry description/tooltip is required for '$($entry.id)' in $descriptorPath"
        if (-not [string]::IsNullOrWhiteSpace($entry.section)) {
            Assert-Condition ($entry.section -match $hanPattern) "Chinese section is required for '$($entry.id)' in $descriptorPath"
        }
        if (-not [string]::IsNullOrWhiteSpace($entry.surfaceDisplayName)) {
            Assert-Condition ($entry.surfaceDisplayName -match $hanPattern) "Chinese surfaceDisplayName is required for '$($entry.id)' in $descriptorPath"
        }
        if (-not [string]::IsNullOrWhiteSpace($entry.surfaceActionLabel)) {
            Assert-Condition ($entry.surfaceActionLabel -match $hanPattern) "Chinese surfaceActionLabel is required for '$($entry.id)' in $descriptorPath"
        }
        Assert-Condition ($allowedCategories -contains $entry.category) "Invalid category for '$($entry.id)' in $descriptorPath"
        if (-not [string]::IsNullOrWhiteSpace($entry.mountModuleId)) {
            Assert-Condition ($entry.mountModuleId -match $stableModuleIdPattern) "Invalid mountModuleId for '$($entry.id)' in $descriptorPath"
        }
        Assert-Condition ($allowedKinds -contains $entry.kind) "Invalid kind for '$($entry.id)' in $descriptorPath"
        Assert-Condition ($allowedSafety -contains $entry.safety) "Invalid safety for '$($entry.id)' in $descriptorPath"
        Assert-Condition ($allowedAvailability -contains $entry.availability) "Invalid availability for '$($entry.id)' in $descriptorPath"
        Assert-Condition ($entry.PSObject.Properties.Name -notcontains 'menuPath') "schema v2 forbids menuPath for '$($entry.id)' in $descriptorPath"
        Assert-Condition ($allowedVisibility -contains $entry.visibility) "Invalid visibility for '$($entry.id)' in $descriptorPath"
        $contentType = if ([string]::IsNullOrWhiteSpace($entry.contentType)) { 'action' } else { $entry.contentType }
        Assert-Condition ($allowedContentTypes -contains $contentType) "Invalid contentType for '$($entry.id)' in $descriptorPath"
        if ($contentType -eq 'reference') {
            Assert-Condition ($entry.safety -in @('navigation', 'read-only')) "Reference '$($entry.id)' must be navigation or read-only"
        }
        Assert-Condition ($entry.executionKind -eq 'provider') "First-party entry '$($entry.id)' must use provider execution"
        Assert-Condition ($entry.providerId -match $stableModuleIdPattern) "Invalid providerId for '$($entry.id)' in $descriptorPath"
        Assert-Condition ($entry.actionId -match $stableActionIdPattern) "Invalid actionId for '$($entry.id)' in $descriptorPath"
        $binding = "$($entry.providerId)/$($entry.actionId)"
        Assert-Condition (-not $actionBindings.ContainsKey($binding)) "Duplicate provider action binding '$binding'"
        $actionBindings[$binding] = "$($descriptor.moduleId)/$($entry.id)"
        Assert-Condition ($sourceText.Contains('[EditorToolActionProvider("' + $entry.providerId + '")')) "No provider '$($entry.providerId)' in $($package.name)"
        Assert-Condition ($sourceText.Contains('"' + $entry.actionId + '"')) "No action '$binding' in $($package.name)"
        if ($entry.kind -eq 'window') {
            Assert-Condition ($entry.safety -eq 'navigation') "Window '$($entry.id)' must use navigation safety"
        }
        if ($entry.safety -in @('project-write', 'destructive')) {
            Assert-Condition (-not [string]::IsNullOrWhiteSpace($entry.confirmation)) "Write-capable entry '$($entry.id)' needs confirmation"
            Assert-Condition ($entry.confirmation -match $hanPattern) "Write-capable entry '$($entry.id)' needs a Chinese confirmation"
            Assert-Condition ($entry.visibility -ne 'primary') "Write-capable entry '$($entry.id)' must not be primary"
        }
        if ($entry.safety -eq 'destructive') {
            Assert-Condition ($entry.visibility -eq 'maintenance') "Destructive entry '$($entry.id)' must be maintenance"
        }
    }

    $panels = if ($null -eq $descriptor.panels) { @() } else { @($descriptor.panels) }
    $panelIds = @{}
    foreach ($panel in $panels) {
        Assert-Condition ($panel.id -match $stableActionIdPattern) "Invalid panel id '$($panel.id)' in $descriptorPath"
        Assert-Condition (-not $panelIds.ContainsKey($panel.id)) "Duplicate panel id '$($panel.id)' in $descriptorPath"
        $panelIds[$panel.id] = $true
        Assert-Condition ($panel.displayName -match $hanPattern) "Chinese panel displayName is required for '$($panel.id)' in $descriptorPath"
        Assert-Condition ($panel.description -match $hanPattern) "Chinese panel description is required for '$($panel.id)' in $descriptorPath"
        Assert-Condition ($panel.providerId -match $stableModuleIdPattern) "Invalid panel providerId for '$($panel.id)' in $descriptorPath"
        Assert-Condition ($allowedSafety -contains $panel.safety) "Invalid panel safety for '$($panel.id)' in $descriptorPath"
        Assert-Condition ($allowedAvailability -contains $panel.availability) "Invalid panel availability for '$($panel.id)' in $descriptorPath"
        Assert-Condition ($sourceText.Contains('[EditorWorkspacePanelProvider("' + $panel.providerId + '")]')) "No panel provider '$($panel.providerId)' in $($package.name)"
        Assert-Condition ($sourceText.Contains('"' + $panel.id + '"')) "No panel '$($panel.providerId)/$($panel.id)' in $($package.name)"
    }

    if ($package.name -ne 'com.zerogamestudio.zeroengine.dashboard') {
        $dependencyNames = @($package.dependencies.psobject.Properties.Name)
        Assert-Condition ($dependencyNames -notcontains 'com.zerogamestudio.zeroengine.dashboard') "$($package.name) must not depend on Dashboard"
        if (@($descriptor.entries).Count -gt 0 -or $panels.Count -gt 0) {
            Assert-Condition ($dependencyNames -contains 'com.zerogamestudio.zeroengine.editor-ui') "$($package.name) provider package must depend on editor-ui"
        }
    }
}

$dashboardRoot = Join-Path $RootPath 'com.zerogamestudio.zeroengine.dashboard'
$dashboardPackage = Get-Content -LiteralPath (Join-Path $dashboardRoot 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$dashboardAsmdef = Get-Content -LiteralPath (Join-Path $dashboardRoot 'Editor\ZeroEngine.Dashboard.Editor.asmdef') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Condition ($dashboardPackage.version -eq '4.7.0') 'Dashboard package version must be 4.7.0.'
$dashboardDependencies = @($dashboardPackage.dependencies.psobject.Properties)
Assert-Condition ($dashboardDependencies.Count -eq 1) 'Dashboard package must depend only on editor-ui.'
Assert-Condition ($dashboardDependencies[0].Name -eq 'com.zerogamestudio.zeroengine.editor-ui' -and $dashboardDependencies[0].Value -eq '1.5.0') 'Dashboard editor-ui dependency must be exactly 1.5.0.'
$dashboardReferences = @($dashboardAsmdef.references)
Assert-Condition ($dashboardReferences.Count -eq 1 -and $dashboardReferences[0] -eq 'ZeroEngine.EditorUI.Editor') 'Dashboard production asmdef must reference only editor-ui.'
Assert-Condition (@($dashboardAsmdef.includePlatforms).Count -eq 1 -and $dashboardAsmdef.includePlatforms[0] -eq 'Editor') 'Dashboard production asmdef must be Editor-only.'

$dashboardProductionText = @(Get-ChildItem -LiteralPath (Join-Path $dashboardRoot 'Editor') -Recurse -File -Filter '*.cs' |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$forbiddenDashboardTokens = @(
    'UnityWebRequest',
    'System.Net.Http',
    'WebClient',
    'PackageManager.Client.Add',
    'PackageManager.Client.Remove',
    'File.Write',
    'File.Delete',
    'Directory.Delete',
    'PlayerPrefs.Delete'
)
foreach ($token in $forbiddenDashboardTokens) {
    Assert-Condition (-not $dashboardProductionText.Contains($token)) "Dashboard production code contains forbidden side-effect token '$token'."
}
Assert-Condition (-not $dashboardProductionText.Contains('未声明工具')) 'Installed package rows must not describe installed packages as undeclared.'
Assert-Condition ($dashboardProductionText.Contains('已安装 · 基础能力（无工作台面板）')) 'Installed package rows must separate installation from workspace integration.'

$menuPaths = @(Get-ChildItem -LiteralPath $RootPath -Directory -Filter 'com.zerogamestudio*' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Filter '*.cs' } |
    Where-Object { $_.FullName -notmatch '[\\/]Tests[\\/]' } |
    ForEach-Object { [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8), '\[MenuItem\("([^"]+)"') } |
    ForEach-Object { $_.Groups[1].Value })
$allowedMenuPaths = @('ZGS/工作台', 'Assets/Export to Mod JSON')
foreach ($menuPath in $menuPaths) {
    Assert-Condition ($allowedMenuPaths -contains $menuPath) "First-party top-level menu is not consolidated: $menuPath"
}
Assert-Condition (@($menuPaths | Where-Object { $_ -eq 'ZGS/工作台' }).Count -eq 1) 'Exactly one ZGS/工作台 MenuItem is required.'

$editorUiPackage = Get-Content -LiteralPath (Join-Path $RootPath 'com.zerogamestudio.zeroengine.editor-ui\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Condition ($editorUiPackage.version -eq '1.5.0') 'editor-ui package version must be 1.5.0.'

if (-not [string]::IsNullOrWhiteSpace($ProjectManifest)) {
    $manifest = Get-Content -LiteralPath $ProjectManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $dependencyNames = @($manifest.dependencies.psobject.Properties.Name)
    $legacyInstalled = $dependencyNames -contains 'com.zerogamestudio.zeroengine'
    $modularInstalled = @($dependencyNames | Where-Object {
        $_ -like 'com.zerogamestudio.zeroengine.*' -and
        $_ -notin @('com.zerogamestudio.zeroengine.dashboard', 'com.zerogamestudio.zeroengine.editor-ui')
    })
    Assert-Condition (-not ($legacyInstalled -and $modularInstalled.Count -gt 0)) 'Legacy and modular ZeroEngine packages are mutually exclusive.'
}

Write-Host "PASS Dashboard descriptors=$($descriptorPaths.Count)"
