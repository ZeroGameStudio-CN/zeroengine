# Package Naming And Ownership

This document defines the naming lanes for ZeroGameStudio Unity packages. The
goal is to keep reusable ZeroEngine packages, studio-level services, and
project-specific adapters easy to distinguish in UPM manifests, asmdef
references, C# namespaces, Unity menus, and documentation.

## Naming Lanes

| Lane | UPM package name | Assembly / namespace | Unity menu root | Ownership |
| --- | --- | --- | --- | --- |
| ZeroEngine reusable package | `com.zerogamestudio.zeroengine.<module>` | `ZeroEngine.<Module>` | `ZeroEngine/...` | Reusable engine/framework code that can ship to more than one game. |
| Studio service SDK | `com.zerogamestudio.<service>` | `ZGS.<Service>` | `ZGS/<Service>` | Studio-level services such as analytics, account, publishing, or backend SDKs. |
| Project adapter package | `com.zerogamestudio.<project>.<module>` | `<Project>.<Module>` | `ZGS/Tools/<Project>/...`, localized equivalents such as `ZGS/工具/<Project>/...`, or `<Project>/...` | Bindings, profiles, migrations, editor tools, or runtime adapters that depend on one game project's data and conventions. |

`com.zerogamestudio` is the publisher prefix. `zeroengine`, `analytics`, and
`pob` are product or project lanes under that publisher.

## Decision Rules

Use `ZeroEngine` when the package can compile and be useful without POB assets,
POB scenes, POB save data, POB config paths, or POB-specific enums. A package
in this lane should expose generic extension points and keep project bindings
outside the package.

Use `ZGS` when the package represents a studio service or brand that is not a
ZeroEngine gameplay/framework module. Analytics is the current intended
example: it is a studio telemetry SDK rather than a `zeroengine.*` module.

Use the project name when the package exists to adapt a reusable package to a
specific game. POB bindings should use `com.zerogamestudio.pob.*` packages,
`POB.*` assemblies/namespaces, and project-scoped menus.

Do not use `ZGS` as a catch-all replacement for `ZeroEngine`. `ZGS` is the
studio/service lane; `ZeroEngine` is the reusable engine product lane.

## Validation

Run `.\tools\validate-package-naming-ownership.ps1` before package handoff. The
gate checks package display names, production/editor asmdef names and
rootNamespace values, C# namespace declarations, and Unity `MenuItem` roots.
`MenuItem` paths must be string literals so the static gate can validate their
ownership root.

The gate intentionally treats `com.zerogamestudio.analytics` as the current
`ZGS.Analytics` studio service SDK exception, and treats
`com.zerogamestudio.zeroengine.data-toolkit` / `ZGS.DataToolkit.*` as explicit
known debt. New `ZGS.*` usage inside `com.zerogamestudio.zeroengine.*` packages
should fail unless it is added to the documented debt list as a deliberate
migration decision.

## Consumer Manifest Examples

Reusable ZeroEngine packages:

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.core": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.core#<tested-commit>",
    "com.zerogamestudio.zeroengine.data-toolkit": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.data-toolkit#<tested-commit>"
  }
}
```

POB adapter packages:

```json
{
  "dependencies": {
    "com.zerogamestudio.pob.formula": "file:com.zerogamestudio.pob.formula",
    "com.zerogamestudio.zeroengine.formula": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.formula#<tested-commit>"
  }
}
```

The adapter package may depend on the reusable package, but the reusable package
must not depend on the adapter.

## Current Naming Debt

These items are naming debt to resolve in dedicated migration batches. They do
not block the current package graduation batch.

- `com.zerogamestudio.zeroengine.data-toolkit` currently uses
  `ZGS.DataToolkit.*` assemblies and namespaces. If this remains a ZeroEngine
  reusable package, migrate it to `ZeroEngine.DataToolkit.*`.
- `com.zerogamestudio.analytics` is allowed to keep `ZGS.Analytics` only if it
  is treated as a studio service SDK. If it becomes a ZeroEngine module, migrate
  it to `com.zerogamestudio.zeroengine.analytics` and `ZeroEngine.Analytics`.
- POB-local packages whose UPM name starts with
  `com.zerogamestudio.zeroengine.*`, including
  `com.zerogamestudio.zeroengine.extraction`, should be classified before
  release. If the package is reusable, upstream it to this repository with
  `ZeroEngine.*` assemblies. If it is POB-bound, rename it to
  `com.zerogamestudio.pob.*`.
- POB editor menus should make project ownership explicit, for example
  `ZGS/Tools/POB/...`, `ZGS/工具/POB/...`, or `POB/...`, instead of generic
  `ZGS/Tools/...` or `ZGS/工具/...` entries.

## Migration Rules

Keep naming migrations separate from behavioral changes. Renaming UPM packages,
asmdefs, namespaces, or serialized types can affect assembly references,
consumer manifests, Unity serialized data, and package lock files.

For each migration batch:

1. Classify the package lane first: ZeroEngine reusable, ZGS service, or project
   adapter.
2. Rename the lowest-risk surface first: README text, display names, and menus.
3. Rename asmdefs and namespaces only when all consumer references can be
   updated in the same batch.
4. Preserve Unity `.meta` files when moving or renaming assets.
5. Update package `README.md`, `CHANGELOG.md`, `package.json`, asmdefs, tests,
   and consumer setup documentation together.
6. Verify the ZeroEngine package tests first, then the narrow consumer-project
   adapter tests.
