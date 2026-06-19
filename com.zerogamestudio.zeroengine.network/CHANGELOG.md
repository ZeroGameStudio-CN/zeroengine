# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added a dedicated Editor assembly and package-level config validation for server configs.

### Changed

- Added this package-level changelog as the baseline for package graduation tracking.
- Declared Netcode, Transport, UGS Core, Authentication, Lobby, and Relay package dependencies used by the network assembly.
- Added a testable command-line parsing entry point and Editor tests for flags, values, duplicate keys, and null input.
