# ZeroEngine Editor UI

Editor-only visual primitives shared by ZeroEngine authoring tools. The package has no runtime, Odin, asset-loading, file, network, or package-discovery dependency.

Git URL consumers must add this package directly to `Packages/manifest.json` at the same ZeroEngine canonical revision as every consuming package. Unity 2022.3 does not resolve this sibling Git package transitively.

Production windows use `EditorUiPalette.Current`, `EditorUiGUILayout`, and `EditorUiElements`. Tests may preview both palette variants through the package test assembly; preview code is not part of the production menu surface.

Version 1.1.0 adds compact headers, action rows, chips, disclosure rows, constrained content, and a deterministic compact/standard breakpoint for Editor dashboards.

Version 1.1.1 adds `GUIContent` overloads for buttons, action rows, chips, selections, and disclosures so package-owned labels can provide localized tooltips.

Version 1.2.0 adds measured inline/stacked action-row layout, a wide responsive mode, and an Editor-only workspace panel SPI. The SPI defines lifecycle and explicit action safety only; discovery and business behavior remain outside this package.

Version 1.3.0 adds the Editor-only tool action SPI used by Dashboard schema v2. Providers are discovered by stable attributes, create typed actions lazily, expose read-only availability state, and return explicit execution results without menu or reflection dependencies.

Version 1.4.0 adds `IEditorWorkspaceNavigator`, allowing typed action providers to request an in-place workspace panel without referencing the Dashboard package or opening another window.
Workspace panels that need a canvas layout can also implement `IEditorWorkspaceFullWidthPanel`; form-style panels remain width-constrained by default.

The 1.4.0 workspace contract also includes the typed `EditorWindowWorkspacePanel<TWindow>` adapter for reusing an IMGUI EditorWindow view inside a workspace. It creates only the active hidden view, supports optional explicit `EditorPrefs` state, and destroys the view when the panel deactivates.

Version 1.5.0 adds typed workspace routes with optional owner-defined subroutes and source context. `IEditorWorkspaceRouteNavigator` moves within the existing host, `IEditorWorkspaceRouteReceiver` applies an owner panel's declared deep link, and `EditorWindowWorkspacePanel<TWindow>` forwards routes without creating a second window. Route actions remain navigation-only and do not bypass the target panel's write or destructive confirmation.
