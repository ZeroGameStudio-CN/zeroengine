# ZeroEngine.Social

Social and relationship systems.

## Modules

- **Relationship** - NPC affinity and gift system
- **Notification** - In-game notification system

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support
- `com.zerogamestudio.zeroengine.economy` - Inventory item definitions and gift consumption

## Version

2.0.1 - Declare the Economy dependency already required by the runtime assembly.

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Explicit owner runtime (3.0 candidate)

`RelationshipState` owns one actor's relationship progress. Inject a stable owner id,
NPC definition resolver and game-calendar `Func<DateTime>`. Advance that calendar
explicitly from the consumer's existing day-change route; no system-clock polling
or implicit real-time default is used. Queries and snapshots return deep copies.

Commands capture a runtime epoch, revision, date and request identity. Repeating the
last committed command returns its receipt; older contexts and conflicting reuse
are refused. Restoring state regenerates its epoch, so pre-load requests expire.
Gift commands take an explicit InventoryManager and join Economy's prepared commit;
relationship progress, counters and the receipt are visible before inventory
observers. There is no fallback inventory or save singleton.

`RelationshipSaveParticipant` supplies the current Persistence contracts. The
application owns its stable key, registration and resource lifetime. The optional
Unity host is explicitly configured and is not a global singleton. This is a
breaking replacement of the old implicit RelationshipManager save/clock/runtime
entry, so consumers adopting 3.0 must migrate calls and review their saved schema.
Existing consumers pinned to an older commit are not automatically upgraded.
