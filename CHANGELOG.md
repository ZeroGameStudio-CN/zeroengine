# Changelog

ZeroEngine is developed as a multi-package repository. Package versions are
tracked in each package's `package.json`.

## Unreleased

- Expanded `com.zerogamestudio.zeroengine.multiplayer` with Multipass route selection, pre-transport game preparation, post-connect local synchronization, automatic client reconnect/restore, authenticated remote-start confirmation, and reconnect-focused coordinator tests.
- Added root project documentation.
- Added contribution, support, and security guidance.
- Added MIT licensing.
- Normalized package repository metadata for UPM Git dependencies.

## com.zerogamestudio.analytics 1.3.0

- Route feedback upload package names through the configured app id so multiple
  games can share the analytics SDK without POB-specific naming.
- Allow repeated feedback uploads in one process and bound generated feedback
  ZIP size, entry count, log size, and manifest size.
- Keep generated ZIP entry names ASCII-safe while preserving non-ASCII feedback
  text inside the report.

For package-specific changes, inspect the package README, package version, and
Git history for the relevant `com.zerogamestudio.*` directory.
