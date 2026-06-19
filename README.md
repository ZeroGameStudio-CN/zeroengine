# ZeroEngine

[![Unity Tests](https://github.com/liuzqk/zeroengine/actions/workflows/tests.yml/badge.svg)](https://github.com/liuzqk/zeroengine/actions/workflows/tests.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com/releases/editor/archive)

ZeroEngine is the shared Unity codebase I maintain for ZeroGameStudio projects.
It is split into Unity Package Manager packages so a game can pull in only the
systems it needs.

Most of these packages came out of production work on POB: runtime helpers,
editor tooling, data systems, save logic, UI, analytics, and 2D platform
navigation. The repository is kept public so those reusable pieces can be used
and tested outside a single game project.

## Why I Keep This Separate

Unity projects tend to accumulate the same glue code over time: singleton and
event helpers, pooled runtime objects, save data, quests, platform navigation,
UI panels, editor data browsers, and debugging tools. Keeping these systems in
packages makes it easier to reuse them across projects and to test them without
mixing in game-specific content.

## Package Highlights

| Package | Purpose |
| --- | --- |
| `com.zerogamestudio.zeroengine.core` | Core helpers, logging, pooling, singleton patterns, and performance utilities. |
| `com.zerogamestudio.zeroengine.data` | Data and stat systems for game configuration and runtime values. |
| `com.zerogamestudio.zeroengine.data-toolkit` | Editor tooling for browsing, inspecting, and validating project data assets. |
| `com.zerogamestudio.zeroengine.gameplay` | Reusable gameplay mechanics and trigger helpers. |
| `com.zerogamestudio.zeroengine.narrative` | Quest and narrative runtime services. |
| `com.zerogamestudio.zeroengine.pathfinding2d` | 2D platform navigation, graph generation, jump links, route costs, and diagnostics. |
| `com.zerogamestudio.zeroengine.persistence` | Save and persistence infrastructure. |
| `com.zerogamestudio.zeroengine.ui` | Runtime UI framework and toast notification systems. |
| `com.zerogamestudio.analytics` | Self-hostable analytics and bug feedback SDK. |

There are also packages for AI, audio, combat, economy, input, localization,
network, RPG, social, world, and editor dashboard systems.

## Installation

Add packages through Unity Package Manager using a Git URL with the package
path you need. Consumer projects should use GitHub as the source of truth, not
a copied `ZeroEngine` folder from a local checkout:

```text
https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.core#<tested-commit>
```

For example, a project `Packages/manifest.json` can pin several packages:

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.core": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.core#<tested-commit>",
    "com.zerogamestudio.zeroengine.pathfinding2d": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.pathfinding2d#<tested-commit>",
    "com.zerogamestudio.zeroengine.ui": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.ui#<tested-commit>"
  }
}
```

Keep all ZeroEngine package entries in a consumer project on the same tested
commit unless you are deliberately validating a version split. See
[Consumer Project Setup](docs/consumer-project-setup.md) for the upgrade flow
and the narrow case where a temporary local `file:` dependency is acceptable.
See [Package Naming And Ownership](docs/package-naming-ownership.md) for the
ZeroEngine, ZGS service, and project-adapter naming lanes.

## Repository Layout

Each top-level `com.zerogamestudio.*` directory is a UPM package. Most packages
follow the same structure:

```text
com.zerogamestudio.zeroengine.<module>/
  Runtime/
  Editor/
  Tests/
  Samples~/
  package.json
  README.md
```

Not every package has every folder; small runtime-only packages stay minimal.

## Testing

The repository includes a GitHub Actions workflow that builds a temporary Unity
project and runs EditMode tests through GameCI.

For local work, open a Unity 2022.3 project that references the package under
test, then run the relevant EditMode tests from Unity Test Runner. Keep new
tests narrow and package-scoped.

## Support and Security

- Use [GitHub issues](https://github.com/liuzqk/zeroengine/issues) for
  reproducible bugs and focused feature requests.
- See [SUPPORT.md](SUPPORT.md) for the information maintainers need.
- See [SECURITY.md](SECURITY.md) for private security reporting guidance.

## Production Use

POB depends on multiple ZeroEngine packages, including analytics, core, data,
data-toolkit, economy, gameplay, narrative, pathfinding2d, persistence, and UI.
That is the main production feedback loop for this repository.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) before opening issues or pull requests.
Small, tested fixes are preferred over broad rewrites.

## License

ZeroEngine is available under the [MIT License](LICENSE).
