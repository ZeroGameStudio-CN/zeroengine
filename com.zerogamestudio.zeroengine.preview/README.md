# ZeroEngine Preview

Portable control of registered development previews. Runtime code contains no project types or player-storage APIs. The Editor panel is an adapter-driven view hosted by ZeroEngine Editor UI.

Each `PreviewRegistration` combines one stable `PreviewDefinition` and one `IPreviewAdapter`. Definitions distinguish toggles, parameterized selections and informational entry points. A `PreviewController` validates requests, rejects production mutations, serializes pending operations and resets adapters on failure. `PreviewEditorLifetime` ends project operations before PlayMode exit and assembly reload. Supply its `IsTransitioning` to the host context so entering and leaving PlayMode both reject new mutations. Lifecycle reset on world teardown can differ from a live-session failure/disposal reset; the latter should restore any replaced runtime selection. Adapters restore only state and resources they own; reset must be idempotent. A reset while an async start is pending blocks replacement starts until that original operation completes and is cleaned.

`PreviewRuntime.Feature(id, productionEnabled)` supplies an explicit production default and an Editor-only session override. Defaults remain authoritative in Player. Runtime subsystem registration clears session overrides and event listeners for fast Play; hosts rebind their listeners after entering Play. Project wrappers should expose primitive values so consumers need not acquire an Editor dependency.

`PreviewControlsPanel` reads the same registrations used by command adapters. Selection previews require a unique candidate ID; merely viewing or querying never selects candidates, writes preferences or edits production content. A host may offer an explicit Dashboard-only startup preference through its adapter. That preference is separate from Console/session overrides and player saves.

The panel derives any number of preview groups from `PreviewDefinition.Group`. A host may use names such as `正式版` or `下个版本` to organize a development batch and pass an initial group to `PreviewControlsPanel(controller, initialGroup)`. Filtering keeps the original controller, adapters and independent feature controls; it never starts a group or changes release status. Use the content registry for actual version targets and release evidence.

Development progress, target version, actual release and production activation remain separate. Use the existing Development Content package for the content registry and version plan. `PreviewAdoptionPlan` reports each feature's production binding, permanent resources, development-only integration and required checks. It does not adopt a feature, change a release status or delete arbitrary source files. An unregistered binding is reported as incomplete, never inferred from a `Preview` filename.

Preview IDs are stable, lowercase identifiers; `all` is reserved for read-only group queries. Undefined capability values are rejected at declaration. Controllers and adapters run on the Unity main thread. Async adapters must bound their work and settle pending tasks during reset; the controller fences late completions instead of starting an overlapping replacement.

## Host integration

1. Register definitions and project adapters in one composition root.
2. Supply an honest development/play/transition context.
3. Keep feature-specific asset replacement, character changes and save isolation in those adapters.
4. Attach one editor lifetime for that controller and provide it to the existing Dashboard/workspace.
5. Declare adoption/cleanup boundaries before promoting the feature. Finish candidate validation and target-specific runtime checks before changing production references.

Package tests use synthetic adapters and data; they cover identity, invalid queries, production rejection, candidate selection, async reset races, failed starts, cleanup failures and read-only adoption reports. They never edit project/user configuration.

## Installation

Consumers should use `https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.preview#<tested-commit>` after the package is published, and declare its Editor UI dependency explicitly. A local `file:` dependency is for temporary validation only. It must be replaced with a tested Git pin before delivery; creating source or passing tests does not publish it.

To run package tests, include `com.zerogamestudio.zeroengine.preview` in the consumer manifest's `testables` array and run `ZeroEngine.Preview.Tests.PreviewContractTests`. See [Unity package testing](https://docs.unity3d.com/cn/current/Manual/cus-tests.html).
