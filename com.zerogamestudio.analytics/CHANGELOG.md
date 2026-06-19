# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added this package-level changelog as the baseline for package graduation tracking.
- `AnalyticsBootstrap` now initializes from an explicit `ZGSAnalyticsConfig` instead of loading a config asset from `Resources`.
- The editor dashboard now finds config assets by type and creates them under `Assets/ZGSAnalytics`.
- Added Editor tests for disabled and null analytics bootstrap configs.
- Added package-level config validation for analytics configs.
- Added package author and keyword metadata.
- Updated the README version badge to match `package.json`.
