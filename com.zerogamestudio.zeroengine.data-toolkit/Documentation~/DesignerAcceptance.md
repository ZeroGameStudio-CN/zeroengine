# Data Toolkit Designer Acceptance

Run this checklist before a production project pins a new Data Toolkit release.

## Basic Navigation

- Open the project Data Manager menu.
- Search for a data type by partial name.
- Select a type with at least 50 assets.
- Search assets by partial name.
- Select an asset and confirm the inspector opens without freezing the editor.

## Stability

- Click Refresh and confirm the selected type and asset are restored when still valid.
- Close and reopen the window and confirm the previous selection is restored.
- Resize both side columns and confirm the inspector remains visible.
- Select a large asset and confirm full inspector loading is deferred until requested.

## Editing

- Edit a common field on a first-class inspector.
- Use Undo and confirm the field returns to the previous value.
- Ping the selected asset and confirm the Project window locates it.

## Diagnostics

- Open Diagnostics.
- Confirm type count, asset count, and coverage levels are visible.
- Confirm no row reports Unsupported for a commonly edited production data type.
- Confirm NoAssets appears only for intentionally empty data types.

## Release Result

Record the release commit, project name, date, tester, and any blocking issue in the project release notes.
