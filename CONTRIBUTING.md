# Contributing to ZeroEngine

ZeroEngine is a multi-package Unity repository. Keep changes scoped to the
package and behavior you are improving.

## Development Notes

- Use Unity 2022.3 LTS unless a package explicitly documents a newer
  requirement.
- Prefer package-local changes over framework-wide abstractions.
- Keep runtime code in `Runtime/` and editor-only code in `Editor/`.
- Include `.meta` files whenever Unity assets, folders, asmdefs, samples, or
  tests are added, moved, or deleted.
- Do not edit generated Unity folders such as `Library/`, `Temp/`, or generated
  IDE project files.

## Package Metadata

Each package should keep a valid `package.json` with:

- `name`, `version`, `displayName`, `description`, `unity`, and `author`.
- `license` set to `MIT`.
- `repository.url` pointing to `https://github.com/liuzqk/zeroengine.git`.
- `repository.directory` matching the top-level package folder.

## Testing

For runtime logic, add focused EditMode tests under the package `Tests/Editor/`
folder. Use PlayMode tests only when Unity lifecycle, physics, scenes, prefabs,
or coroutines are required.

The GitHub workflow builds a temporary Unity project and runs EditMode tests
with GameCI. Local verification can use Unity Test Runner with the smallest
relevant package test assembly.

## Pull Requests

- Explain the user-facing reason for the change.
- List the package or packages touched.
- Include the verification you ran.
- Keep unrelated formatting and refactors out of the PR.

## License

By contributing, you agree that your contribution is provided under the
repository's MIT License.
