# ZeroEngine Data Toolkit

ZeroEngine Data Toolkit is a reusable Unity package for marking, browsing, searching, inspecting, and validating project data assets.

## Install

Add the package through Unity Package Manager using the Git URL for this repository and the `com.zerogamestudio.zeroengine.data-toolkit` path. Production projects should pin a release tag or reviewed commit.

Version 2.1.1 adds project-configurable Data Toolkit UI text while keeping English defaults. It requires a direct Git pin for `com.zerogamestudio.zeroengine.editor-ui@1.4.0` at the same commit. Unity 2022.3 does not resolve same-repository sibling packages transitively.

Version 2.0.1 localizes the Dashboard host label and description to Simplified Chinese; project adapters and menu routes remain compatible.

## Embedded Workspace Panel

Dashboard adapters can reuse the complete Data Toolkit view without opening another window:

```csharp
return new DataToolkitWorkspacePanel(ExampleDataToolkitRegistration.CreateProfile);
```

The embedded panel fills the workspace canvas and shares the standalone window's inspectors and `EditorPrefs` state, including selected type/asset, searches, column widths, and scroll positions.

## Project Profile

Projects register a profile at editor load time:

```csharp
using UnityEditor;
using ZGS.DataToolkit.Editor;

[InitializeOnLoad]
public static class ExampleDataToolkitRegistration
{
    static ExampleDataToolkitRegistration()
    {
        DataToolkitProjectRegistry.RegisterDefault(CreateProfile);
    }

    public static DataToolkitProjectProfile CreateProfile()
    {
        return new DataToolkitProjectProfile(
            new DataToolkitProjectSettings(
                projectId: "Example",
                windowTitle: "Example Data Manager",
                menuPath: "Tools/Data Manager",
                editorPrefsPrefix: "Example_DataManager",
                searchRoots: new[] { "Assets/Data" },
                excludedPaths: new[] { "Assets/Data/Generated" },
                defaultInspectorMode: DataToolkitDefaultInspectorMode.LazyPreview,
                uiText: new DataToolkitUiText(dataTypes: "数据类型", assets: "资产")));
    }
}
```

## Manageable Data

Data types are shown when they are non-abstract `ScriptableObject` types marked with `ZGS.DataToolkit.ManageableDataAttribute`. Existing projects that already define a project-local `ManageableDataAttribute` remain supported for compatibility.

```csharp
using UnityEngine;
using ZGS.DataToolkit;

[ManageableData]
public sealed class ItemData : ScriptableObject
{
}
```

### Editor-only type registration

Version 2.2.0 adds explicit registration for project and vendor ScriptableObjects that cannot carry an attribute. Create a project settings copy with the additional types:

```csharp
var settings = new DataToolkitProjectSettings(
    projectId: "Example", windowTitle: "Example Data Manager",
    menuPath: "Tools/Data Manager", editorPrefsPrefix: "Example_DataManager",
    searchRoots: new[] { "Assets/Data" }, excludedPaths: System.Array.Empty<string>())
    .WithAdditionalManageableTypes(new[]
    {
        typeof(UnityEngine.Timeline.TimelineAsset),
        typeof(UnityEngine.Rendering.VolumeProfile)
    });
```

Register types in the project's Editor assembly, referencing each type's owning assembly there. The toolkit has no Timeline, render-pipeline or gameplay dependency. The type list is copied, filtered to concrete closed ScriptableObject types and deduplicated. It augments the existing attributed types in the window and diagnostics. Reapplying the method replaces the explicit list on a new settings copy; it does not modify the original settings or global attributed discovery. The original constructor and no-argument discovery API remain available.

Asset searches continue to use the active settings' roots and exclusions. Their cache is scoped to the settings instance, so two profiles with the same project ID do not share results from different roots. Explicit registration does not expand asset search roots or provide a specialized inspector: existing providers and Unity's native fallback keep ownership of editing.

## Inspector Coverage

Custom inspector providers can make high-value data types first-class. Types without custom providers fall back to the configured default inspector mode. Use **Diagnostics** in the window header to review current type coverage and asset counts.

## Release Checklist

Before updating a production project:

1. Run Data Toolkit package EditMode tests.
2. Run the consuming project's Data Manager integration tests.
3. Review `CHANGELOG.md`.
4. Run the designer acceptance checklist in `Documentation~/DesignerAcceptance.md`.
5. Pin the consuming project to a reviewed tag or commit.
