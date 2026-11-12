# ZeroEngine AI Support

This optional Editor package synchronizes project guidance from the packages
actually installed in a Unity project. It adds no Player assembly, watcher,
automatic install, global Skill or machine configuration.
Its read-only snapshot exporter uses Unity's declared built-in JSON serialization module.

Install through a pinned Git UPM URL, using the same tested commit as the other
ZeroEngine packages in the consumer. See the repository's consumer rules.

1. In the project's existing Editor/CLI route, evaluate
   `ZeroEngine.AiSupport.Editor.SkillPackageSnapshot.CreateJson()` and save its
   returned JSON to an external file. This is read-only and uses actual
   `PackageInfo`, the manifest and lock digests.
2. Run the installed package's `Tools~/sync_skill.py` with Python 3.10+:

   ```text
   python -B <resolved-package>/Tools~/sync_skill.py --project <project> --snapshot <snapshot.json>
   python -B <resolved-package>/Tools~/sync_skill.py --project <project> --snapshot <snapshot.json> --apply
   ```

The first command previews a project-local `.agents/skills/zeroengine` update.
`--check` returns 0 for current guidance, 3 for an available update and 2 for a
conflict. `--apply` writes only generated guidance; it does not resolve packages
or change `Packages`, assets, project settings, other Skills or global files.

Existing unowned guidance, changed managed files, stale package snapshots, newer
tool versions and unknown receipt schemas stop the update. Local additions are
preserved. A failed write restores unchanged owned preimages; a conflicting new
write is preserved with a recovery directory rather than overwritten.

Normal consumer projects should use one tested commit. An explicitly authorized
version-split investigation may pass `--allow-version-split`; each module still
gets its own actual version and source. `--include-local-trials` includes existing
local/embedded trials as unverified local sources. Neither flag changes a pin or
certifies that the combination is compatible. Replacing a clean receipt with a
different commit at the same tool version requires `--accept-current-commit`
after reviewing that exact installed source; a semantic-version downgrade stays
blocked.

Package instructions live in `Skills~`; deterministic synchronization and its
isolated CLI tests live in `Tools~`. The exported project Skill links to the
installed package inventory and copies only those packages' own READMEs for
progressive lookup. Missing modules never become claimed capabilities.
