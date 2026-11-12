# ZeroEngine leaderboard server kernel

This is a Python server library, not a Unity UPM package. Its source
home is `Services/leaderboards/` in the ZeroEngine repository. Do not ship it,
publisher credentials, or signing keys in a game client.

The kernel owns authenticated run identity, signed receipts, nonce/sequence
ordering, idempotent delivery, atomic event batches, publication state and
bounded history retention. It contains no POB board naming, weekly seed,
content catalogue, combat simulation or anti-cheat rules.

## Game boundary

Use a separate service instance, ledger/database and signing key per game.
Share the machine and library, not player credentials or a writable ledger.
There is no tenant router, central account service or shared admin interface.

A game subclasses `ze_leaderboards.core.RunService` and supplies:

- `validate_policies(config)`: admit only explicit server-owned boards/rules.
- `_apply_progress(db, row, body, policy)`: validate gameplay observations and
  derive `(cleared, state, score, policy, assessment)`; never accept a final
  client-selected score as proof of play.
- An `authenticator(ticket)` that verifies the platform credential and returns
  a stable subject, or raises `AuthenticationRejected`/`ProviderUnavailable`.
- A publisher's `set_score(policy, subject, score)` for the actual destination.
  If authentication is not separately supplied, the existing combined adapter's
  `authenticate(ticket)` remains supported.

Optional template and normalization hooks are for actual game rules. Unknown
boards stay rejected unless the game explicitly supplies an approved template.
The authentication token is opaque to the kernel. A mobile game can verify its
own platform ticket and use another publisher without importing the Steam SDK.
This is an extension boundary, not a shipped mobile identity provider or a
cross-platform leaderboard storage/query API.

`steam.SteamPublisher` is an optional adapter. It requires an explicit AppID,
ticket identity, private key file and descending-mode set. It only calls the
fixed Steam publisher host; a client request cannot select the AppID or host.

## Compatibility and delivery

The existing v1 run JSON, event/batch wire format, legacy room-flow version and
SQLite schema are retained. The historical `steam_id` column stores the verified
subject; it does not require a Steam identity when using another authenticator.
Do not point another game at an existing POB database to reuse that schema.

An ambiguous publisher response remains `unknown` and is not blindly retried.
Only `WriteNotSent` means that no score request was dispatched and allows the
existing safe publication retry path. Publishers must bound their calls; the
shared stale-publication grace must exceed that bound.

POB keeps its own `service.py` rules, `steam_api.py` identity defaults and
`app.py` deployment/HTTP adapter. Its current credentials, API, policies and
database need no migration. A future POB release must include the exact pinned
`ze_leaderboards` package snapshot alongside these adapters and record its source
revision/hash. No service deployment or restart is part of source extraction.

For a standalone source bundle, run
`python -B Services/leaderboards/export_snapshot.py --revision <tested-commit> --output <new-directory>`
from the repository. Review `zeroengine-source.json` and consume those exact
files; deployment never downloads floating source or a developer's checkout.
For a separately installed Python package, pin the repository commit and
`subdirectory=Services/leaderboards`. Neither path changes Unity package pins.

## Maintenance and recovery

The HTTP adapter must not call history pruning after admitting a run. Schedule
`maintenance.prune_history(ledger, now)` separately. It uses a persistent indexed
expiry cursor, a deferred-hold queue and short bounded delete transactions. It
preserves the existing expiry plus three-day grace, start/final/last-batch
receipts, run identities and held publication/review states. It does not delete
whole runs or scores. New metadata tables/indexes preserve the legacy `runs`
column layout for code rollback. A busy maintenance writer yields; an API
SQLite busy/locked error becomes `LedgerUnavailable` (HTTP 503), without retrying
the transaction or reissuing an external score write.

`recovery.backup(layout, output, timestamp)` makes an online SQLite snapshot,
includes the pinned runtime package and source manifest, streams both archive
creation and verification, and rotates the existing one-database/three-bundle
local retention only after validation. Keep outputs private: bundles contain
keys. Game adapters own layout paths, CLI defaults and systemd units. A restore
drill must import the restored runtime in isolation; never test a restored
publisher against a production leaderboard. Offsite replication and mobile
ranking storage remain separate planned work.

## Verification

Run `python -B -m unittest discover -s tests` here. The independent synthetic
stage game verifies non-Steam subjects, server-derived scores, separate game
ledgers/keys, replay safety and ambiguous publication. POB's existing service
suite must also pass against the same library before POB adopts a snapshot.
