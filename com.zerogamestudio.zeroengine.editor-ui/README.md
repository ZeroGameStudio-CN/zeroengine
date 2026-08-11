# ZeroEngine Editor UI

Editor-only visual primitives shared by ZeroEngine authoring tools. The package has no runtime, Odin, asset-loading, file, network, or package-discovery dependency.

Git URL consumers must add this package directly to `Packages/manifest.json` at the same ZeroEngine canonical revision as every consuming package. Unity 2022.3 does not resolve this sibling Git package transitively.

Production windows use `EditorUiPalette.Current`, `EditorUiGUILayout`, and `EditorUiElements`. Tests may preview both palette variants through the package test assembly; preview code is not part of the production menu surface.

Version 1.1.0 adds compact headers, action rows, chips, disclosure rows, constrained content, and a deterministic compact/standard breakpoint for Editor dashboards.

Version 1.1.1 adds `GUIContent` overloads for buttons, action rows, chips, selections, and disclosures so package-owned labels can provide localized tooltips.

Version 1.2.0 adds measured inline/stacked action-row layout, a wide responsive mode, and an Editor-only workspace panel SPI. The SPI defines lifecycle and explicit action safety only; discovery and business behavior remain outside this package.

Version 1.3.0 adds the Editor-only tool action SPI used by Dashboard schema v2. Providers are discovered by stable attributes, create typed actions lazily, expose read-only availability state, and return explicit execution results without menu or reflection dependencies.
