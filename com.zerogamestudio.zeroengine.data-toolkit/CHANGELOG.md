# Changelog

## Unreleased

- Changed full inspector fallback to prefer Unity native editors before Odin reflection, so project `[CustomEditor]` implementations are respected inside Data Toolkit.
- Added diagnostics coverage for native inspector fallback.

## 1.1.0 - 2026-06-06

- Added package-native `ZGS.DataToolkit.ManageableDataAttribute`.
- Added read-only Data Toolkit diagnostics for manageable type coverage and asset counts.
- Added a diagnostics window entry from the main Data Toolkit header.
- Added package README and designer acceptance checklist.
- Updated CI plan so package tests include Data Toolkit test assemblies.

## 1.0.0

- Initial reusable Data Toolkit package with project profiles, custom inspector providers, safe/lazy preview modes, selection persistence, and stable window layout.
