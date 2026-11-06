# ZeroEngine.Narrative

Narrative systems for story-driven games.

## Modules

- **Dialog** - Node-based dialogue system with branching and conditions
- **Quest** - Quest management with objectives and rewards
- **Achievement compatibility** - Legacy inventory conditions/rewards, optional slot
  adapter and assembly forwarding for saved achievement types

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.achievement` - Independent achievement runtime
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support

## Version

3.0.0 - Achievement runtime moved into its own package. Keep the namespace and asset
GUIDs. Projects that relied on automatic slot registration must bind the explicit
`AchievementSaveSlotAdapter`. Steam providers now require explicit registration.

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)
