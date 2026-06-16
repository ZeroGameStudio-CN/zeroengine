# ZeroEngine

[![Unity Tests](https://github.com/liuzqk/zeroengine/actions/workflows/tests.yml/badge.svg)](https://github.com/liuzqk/zeroengine/actions/workflows/tests.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com/releases/editor/archive)

ZeroEngine is a modular Unity game framework maintained by ZeroGameStudio. It
is organized as a set of Unity Package Manager packages so projects can depend
on only the systems they need.

The packages are developed against Unity 2022.3 LTS and are used by the POB
Unity project for production gameplay tooling and runtime systems.

## Why This Exists

Unity game projects often rebuild the same infrastructure: singleton and event
helpers, pooled runtime objects, save data, quests, platform navigation, UI
panels, editor data browsers, and game-specific debugging tools. ZeroEngine
keeps those systems in reusable packages with focused tests instead of burying
them inside one game repository.

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

Additional packages in this repository cover AI, audio, combat, economy,
input, localization, network, RPG, social, world, and editor dashboard systems.

## Installation

Add packages through Unity Package Manager using a Git URL with the package
path you need:

```text
https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.core#main
```

For example, a project `Packages/manifest.json` can pin several packages:

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.core": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.core#main",
    "com.zerogamestudio.zeroengine.pathfinding2d": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.pathfinding2d#main",
    "com.zerogamestudio.zeroengine.ui": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.ui#main"
  }
}
```

Production projects should pin a tested commit hash instead of `#main`.

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

## Support And Security

- Use [GitHub issues](https://github.com/liuzqk/zeroengine/issues) for
  reproducible bugs and focused feature requests.
- See [SUPPORT.md](SUPPORT.md) for the information maintainers need.
- See [SECURITY.md](SECURITY.md) for private security reporting guidance.

## Current Production Users

The POB Unity project depends on multiple ZeroEngine packages, including
analytics, core, data, data-toolkit, economy, gameplay, narrative,
pathfinding2d, persistence, and UI. This gives the packages a real production
feedback loop while keeping reusable code outside the game-specific repository.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) before opening issues or pull requests.
Small, tested fixes are preferred over broad rewrites.

## License

ZeroEngine is available under the [MIT License](LICENSE).
