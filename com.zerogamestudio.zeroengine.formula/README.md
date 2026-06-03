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

Use a pinned commit for production projects.

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
ZeroEngine/Formula/Formula Workbench
```

The scanner evaluates formula assets with an empty provider registry and reports
missing providers, invalid nested formulas, non-finite results, and other
structural issues.

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

These APIs are editor-only and do not change runtime formula evaluation.

## Package Boundary

Runtime code must stay free of project-specific dependencies:

- no `POB` namespace references
- no Odin/Sirenix references
- no `UnityEditor` references

Project integrations should live in project-specific packages or assemblies and
adapt their game data into `IFormulaValueProvider` implementations.
