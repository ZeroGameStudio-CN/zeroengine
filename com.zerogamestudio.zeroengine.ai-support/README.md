# ZeroEngine Agent

Optional Editor guidance for all installed ZE modules. The display name is
**ZeroEngine Agent**; the existing package ID
`com.zerogamestudio.zeroengine.ai-support` and `ZeroEngine.AiSupport.Editor` API
stay compatible. No Player assembly or background service is added.

`Skills~` maintains the project Skill template; `Tools~` provides its lifecycle
CLI. Module READMEs remain the API source. Generated consumer guidance lives in
`.agents/skills/zeroengine`, with an inventory and copied module READMEs.

## Packages and guidance

Package installation, upgrades and removal use the consumer's existing UPM route
(`UnityEditor.PackageManager.Client` or Package Manager UI). Keep ZE Git packages
at one tested commit and include required sibling dependencies.

After a package change, export
`ZeroEngine.AiSupport.Editor.SkillPackageSnapshot.CreateJson()` through that
project's Editor executor to an external file. Then use Python 3.10+:

```text
python -B <resolved-package>/Tools~/sync_skill.py --project <project> --snapshot <snapshot.json>
python -B <resolved-package>/Tools~/sync_skill.py --project <project> --snapshot <snapshot.json> --apply
python -B <resolved-package>/Tools~/sync_skill.py --project <project> --snapshot <snapshot.json> --check
```

Preview is read-only. Check returns 0 for current guidance, 3 for drift and 2 for
a conflict. Apply changes generated guidance only; it never repins packages or
changes global Skills, assets or project settings. Removed module references are
cleaned when unchanged. Local additions remain; stale snapshots, modified managed
files, unknown/newer receipts and failed restoration stop with recovery evidence.

Explicit trials use `--include-local-trials` / `--allow-version-split` and keep
their provenance. A same-tool-version commit change requires reviewed installed
source and `--accept-current-commit`; semantic-version downgrades are refused.

## Remove generated guidance

Before uninstalling Agent, run the installed package tool, then remove it via UPM:

```text
python -B <resolved-package>/Tools~/sync_skill.py --project <project> --remove
python -B <resolved-package>/Tools~/sync_skill.py --project <project> --remove --apply
```

Removal uses its managed-file receipt without a snapshot, preserves custom files
and refuses changed ownership. Run the installed tool, not the copy inside the
Skill being removed. It removes guidance only. Known failures restore preimages.
