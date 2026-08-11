# ZeroEngine Config Pipeline

Schema-first configuration pipeline for Unity 2022.3 projects. It keeps one
authoring source, validates through a typed intermediate document, emits
deterministic artifacts, and loads immutable runtime snapshots.

Version 2.0.1 uses `com.zerogamestudio.zeroengine.editor-ui@1.3.0` for its Editor window and typed workbench action. Git URL consumers must directly pin both packages to the same ZeroEngine commit because Unity 2022.3 does not resolve same-repository sibling dependencies transitively.

Version 2.0.1 localizes the Dashboard module label, description, and tooltip to Simplified Chinese without changing the menu route.

Start with `Documentation~/PROJECT_INTEGRATION.md`. The 1.0 contract, Excel
authoring rules, CI commands, recovery behavior and AI maintenance workflow are
all documented under `Documentation~/`. Import `Minimal Item Drop` from Package
Manager for a project-neutral end-to-end sample. Consuming projects that run the
package tests must expose it through the manifest `testables` entry described in
the integration and CI guides and install the documented test-framework
prerequisite.

## ZeroEngine Dashboard

The optional Dashboard discovers this package through its schema v2 descriptor and invokes the package-owned typed provider. Open it from `ZGS > 工作台 > 内容创作`; this package does not reference or require Dashboard.
