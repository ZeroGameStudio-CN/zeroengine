# Changelog

## 2.7.0

### Added

- Typed top-down exploration contracts for control mode, movement authority, locomotion, `Facing8`, action overlays, ground snapshots, safe poses, and motor snapshots.
- Deterministic direction resolution for input dead zones, eight-way facing, and four-way dominant-axis mapping with tie-band hysteresis.
- Token-based exploration control coordination with priority arbitration, nested leases, idempotent release, and observable token snapshots.
- Pure motor math for locomotion selection, target speed, vertical integration, bounded displacement steps, and blocked detection.
- EditMode coverage for exploration direction, control coordination, and motor math.
