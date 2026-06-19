# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Added

- Added automatic config ScriptableObject discovery for `ManageableData`, `CreateAssetMenu`, and ZeroEngine/ZGS config-like types.
- Added generic data validation for empty stable IDs, duplicate stable IDs, and broken object references.
- Added generic designer config semantic validation for display text, numeric ranges, Min/Max pairs, and empty object-reference collection entries.
- Added `IDataToolkitValidationProvider` so packages can contribute config-specific validation rules to the shared report.
- Added automatic discovery for parameterless validation providers in the generic profile.
- Added runtime adaptation for ZeroEngine/ZGS package-local static `*ConfigValidator` validators so the shared workbench can surface package rules without adding DataToolkit dependencies to every package.
- Added validation state to the DataToolkit window and CSV validation summary export.
- Added field-level CSV import preview and apply flow with Undo/dirty-save handling.
- Added batch field editing for visible assets with dry-run previews.
- Added outgoing/incoming data asset reference graph inspection.
- Added Editor tests for discovery, validation, package-local validator bridge adaptation, CSV escaping, CSV import previews, batch editing, and reference graph inspection.
