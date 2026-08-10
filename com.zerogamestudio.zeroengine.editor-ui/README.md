# ZeroEngine Editor UI

Editor-only visual primitives shared by ZeroEngine authoring tools. The package has no runtime, Odin, asset-loading, file, network, or package-discovery dependency.

Git URL consumers must add this package directly to `Packages/manifest.json` at the same ZeroEngine canonical revision as every consuming package. Unity 2022.3 does not resolve this sibling Git package transitively.

Production windows use `EditorUiPalette.Current`, `EditorUiGUILayout`, and `EditorUiElements`. Tests may preview both palette variants through the package test assembly; preview code is not part of the production menu surface.
