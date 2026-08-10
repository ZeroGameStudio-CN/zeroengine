# ZeroEngine Formula

Reusable step-based formula evaluation for Unity projects.

This package provides a small formula runtime, structured diagnostics, provider
extension points, integer rounding helpers, an asset scanner, and a simple
Editor workbench. It intentionally does not include project-specific stat,
resource, buff, player, or scene providers.

## Installation

Install from the ZeroEngine repository with Unity Package Manager:

```json
"com.zerogamestudio.zeroengine.formula": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.formula#<commit>"
```

Version 0.4.0 also requires a direct pin for `com.zerogamestudio.zeroengine.editor-ui@1.0.0` at the same commit. Unity 2022.3 does not resolve same-repository sibling packages transitively. Use a pinned commit for production projects.

## Runtime Formula

Use `FormulaRuntimeDefinition` when a formula is assembled by code:

```csharp
using ZeroEngine.Formula;

var formula = new FormulaRuntimeDefinition(
    "damage-bonus",
    10f,
    new[]
    {
        FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Constant(5f)),
        FormulaStep.Create(FormulaOperationType.MultiplyFactor, FormulaValueSource.Constant(0.2f)),
    });

var success = FormulaEvaluator.TryEvaluate(
    formula,
    FormulaDictionaryEvaluationContext.Empty,
    FormulaProviderRegistry.Empty,
    out var value,
    out var report);
```

`value` is the evaluated result. `report` contains step traces and diagnostics.

## Injected Random Values

Random formula steps are explicit and require an injected source:

```csharp
var formula = new FormulaRuntimeDefinition(
    "random-reward",
    1f,
    new[]
    {
        FormulaStep.Create(
            FormulaOperationType.Multiply,
            FormulaValueSource.RandomInteger(10, 20)),
    });

var randomSource = new SystemFormulaRandomSource(new System.Random(1234));
FormulaEvaluator.TryEvaluate(
    formula,
    FormulaDictionaryEvaluationContext.Empty,
    FormulaProviderRegistry.Empty,
    randomSource,
    out var value,
    out var report);
```

Integer ranges include both bounds. `SystemFormulaRandomSource` maps `[a, b]`
to `System.Random.Next(a, b + 1)` and consumes one call when `a == b`.
Random formulas fail with a diagnostic when no source is supplied; they never
fall back to global Unity random state. Editor previews and scans use a fixed
seed for repeatable results.

## Provider Example

Projects own their provider ids and provider behavior:

```csharp
using ZeroEngine.Formula;

public sealed class LevelProvider : IFormulaValueProvider
{
    public string Id => "player.level";

    public bool TryGetValue(
        FormulaProviderRequest request,
        IFormulaEvaluationContext context,
        out float value,
        FormulaDiagnosticSink diagnostics)
    {
        value = context.TryGetValue("level", out var level) ? level : 1f;
        return true;
    }
}

var registry = new FormulaProviderRegistry();
registry.Register(new LevelProvider());

var context = FormulaDictionaryEvaluationContext.Empty;
context.SetValue("level", 12f);
```

Formula steps can then use:

```csharp
FormulaValueSource.Provider("player.level")
```

## Formula Assets

Create designer-authored assets through:

```text
Assets/Create/ZeroEngine/Formula/Formula Asset
```

Then evaluate the asset with `FormulaEvaluator.TryEvaluate`.

## Editor Tools

Menu items:

```text
ZeroEngine/Formula/Scan Formula Assets
ZeroEngine/Formula/Formula Catalog
ZeroEngine/Formula/Formula Workbench
```

The scanner evaluates formula assets with an empty provider registry and reports
missing providers, invalid nested formulas, non-finite results, and other
structural issues.

The catalog window lists formula assets for the active profile, combines scan
issues with reference counts and catalog metadata, filters common governance
states, opens the shared Workbench, and can generate missing draft catalog
entries without changing formula math.

## Governance Tools

Project editor profiles can declare:

- a Formula Catalog asset path
- reference roots to scan for formula GUID usage
- excluded roots such as `Library`, `Temp`, or generated output

The editor package includes reusable governance primitives:

- `FormulaCatalogEntry` and `FormulaCatalogLookup` for content metadata
- `FormulaReferenceIndexer` for deterministic GUID reference matches
- `FormulaAssetScanReportExporter` for JSON and Markdown reports
- `FormulaRenamePlanner` for safe rename dry-runs before touching assets
- `FormulaCatalogWindow` for project profile-driven formula catalog review

These APIs are editor-only and do not change runtime formula evaluation.

## Debugging Workflows

Formula preview debugging can be automated with:

- `FormulaPreviewCase` for named sets of preview input values
- `FormulaRuntimeSnapshot` for captured runtime context values
- `FormulaPreviewRunner` for evaluating multiple cases with one formula/profile
- `FormulaPreviewReportExporter` for JSON and Markdown summaries
- `FormulaPreviewCaseAsset` for designer-authored reusable sample inputs
- `FormulaCurvePreview` for x-axis sweep previews over one input key
- `FormulaWorkbenchSession` for Workbench batch preview and report export

These reports are designed for Workbench UI, CI artifacts, and agent-assisted
formula configuration.

## CI And Migration

Batchmode scanner entry:

```text
-executeMethod ZeroEngine.Formula.Editor.FormulaScannerCli.Run
```

Formula CLI flags:

```text
-formulaProfile <profileId>
-formulaReportJson <path>
-formulaReportMarkdown <path>
-formulaFailOnWarning
-formulaFailOnMissingCatalog
```

The CLI exits with `1` for scan errors, `2` for warnings when strict warning
mode is enabled, and `3` for missing catalog warnings when that gate is enabled.

Editor-only migration helpers support deterministic dry-run/apply for provider
id renames and parameter key renames. `FormulaMigrationReportExporter` emits
JSON and Markdown summaries for agent review.

## Package Boundary

Runtime code must stay free of project-specific dependencies:

- no `POB` namespace references
- no Odin/Sirenix references
- no `UnityEditor` references

Project integrations should live in project-specific packages or assemblies and
adapt their game data into `IFormulaValueProvider` implementations.

## ZeroEngine Dashboard

The optional Dashboard discovers Formula Catalog, Formula Workbench, and the read-only asset scan through `Editor/ZeroEngineDashboardModule.json`. Existing menu paths remain the authoritative entrypoints and this package does not depend on Dashboard.
