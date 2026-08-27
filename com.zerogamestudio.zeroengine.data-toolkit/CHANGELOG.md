# Changelog

## 2.1.1 - 2026-08-13

- Added project-configurable Data Toolkit UI text with compatible English defaults.

## 2.1.0 - 2026-08-13

- Added a reusable `DataToolkitWorkspacePanel` for in-place Dashboard embedding.
- Preserved type, asset, search, column width, and scroll state across embedded panel recreation.
- Kept the standalone window API for compatible external consumers without sharing its hidden embedded host.

## 2.0.1 - 2026-08-10

- Localized the Dashboard host label and description to Simplified Chinese.

## 2.0.0 - 2026-08-10

- Unified the Data Toolkit window on `com.zerogamestudio.zeroengine.editor-ui@1.0.0`.
- Git URL consumers must directly pin editor-ui to the same ZeroEngine commit.

## Unreleased

- Changed full inspector fallback to prefer Unity native editors before Odin reflection, so project `[CustomEditor]` implementations are respected inside Data Toolkit.
- Added diagnostics coverage for native inspector fallback.
- Allowed the data type and asset columns to resize down to 64 pixels while long row labels remain clipped.

## 1.1.0 - 2026-06-06

- Added package-native `ZGS.DataToolkit.ManageableDataAttribute`.
- Added read-only Data Toolkit diagnostics for manageable type coverage and asset counts.
- Added a diagnostics window entry from the main Data Toolkit header.
- Added package README and designer acceptance checklist.
- Updated CI plan so package tests include Data Toolkit test assemblies.

## 1.0.0

- Initial reusable Data Toolkit package with project profiles, custom inspector providers, safe/lazy preview modes, selection persistence, and stable window layout.
