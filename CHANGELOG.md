# Changelog

ZeroEngine is developed as a multi-package repository. Package versions are
tracked in each package's `package.json`.

## Unreleased

- Added root project documentation.
- Added contribution, support, and security guidance.
- Added MIT licensing.
- Normalized package repository metadata for UPM Git dependencies.

## com.zerogamestudio.zeroengine.core 2.2.0

- Add injectable log channels, immutable entries, level filtering, and the Unity log sink while preserving the existing `ZeroLog` API and format.

## com.zerogamestudio.zeroengine.core 2.1.0

- Reset registered services during Unity subsystem registration so stale scene instances cannot survive Play Mode entry when Domain Reload is disabled.

## com.zerogamestudio.analytics 1.6.1

- Expose `AnalyticsService.Flush()` so callers can trigger delivery of events
  queued by providers that support explicit flushing.

## com.zerogamestudio.analytics 1.6.0

- Add a durable event queue alongside the existing buffered queue. Durable
  events are persisted immediately and can evict older buffered events when
  the queue is full, so important events survive a crash instead of being
  dropped with the rest of the buffer.

## com.zerogamestudio.analytics 1.5.0

- Split feedback upload authentication from event authentication into a
  dedicated upload secret, sent via an `X-Upload-Secret` header instead of a
  plaintext form field. Falls back to the event secret when unset, so existing
  configurations keep working.

## com.zerogamestudio.analytics 1.4.0

- Retry queued feedback uploads in the background on a schedule, including
  after the app restarts, instead of only retrying within the same session.

## com.zerogamestudio.analytics 1.3.0

- Route feedback upload package names through the configured app id so multiple
  games can share the analytics SDK without POB-specific naming.
- Allow repeated feedback uploads in one process and bound generated feedback
  ZIP size, entry count, log size, and manifest size.
- Keep generated ZIP entry names ASCII-safe while preserving non-ASCII feedback
  text inside the report.

For package-specific changes, inspect the package README, package version, and
Git history for the relevant `com.zerogamestudio.*` directory.
