# ZeroEngine.Social

Social and relationship systems.

## Modules

- **Relationship** - NPC affinity and gift system
- **Notification** - In-game notification system

## Designer Config Validation

`ZeroEngine.Social.Editor` provides `SocialConfigValidator` for relationship
data and relationship groups. It reports duplicate NPC/group IDs, invalid
threshold ordering, liked/disliked gift overlap, broken event definitions, and
duplicate group members.

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support
- `com.zerogamestudio.zeroengine.economy` - Gift and item integration

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
