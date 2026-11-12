# ZeroEngine.EditorTools

Project-neutral editor tool registries, task execution, windows and source guards.
Projects provide their own profiles, commands, production paths and validation policy.

## Exact file rollback

`EditorFileSnapshot(path, absoluteProjectRoot)` captures bytes and existence for one
explicit file. `MatchesCurrent`, `Restore` and `AssertRestored` preserve unchanged
bytes without rewriting timestamps. Restoring a missing parent fails closed unless
the caller explicitly opts into recreation. Absolute temporary paths are supported;
relative paths cannot escape the given project root. Directories are not snapshots.

The snapshot does not discover paths, pair `.meta` files, delete Unity assets, import,
clear dirty state, restore scenes/Undo, acquire write permission or catch an owner's
transaction failure. Capture the reviewed exact asset and metadata list before writing;
keep those policies and error reporting in the authoring owner. Asset-path hints only
help the owner choose its existing import route and never execute it automatically.


## Consolidation dependency correction

The 1.1.1 consolidation candidate explicitly declares Unity's built-in
JSON Serialize module (`com.unity.modules.jsonserialize`) for this package's
direct `JsonUtility` calls. Consumers no longer need an unrelated installed
package to supply that module. This candidate is not a published release.

The Editor assembly also requires Unity Test Framework 1.3.9 or later for its
public test-runner tasks; this dependency is required when the tool is installed.
