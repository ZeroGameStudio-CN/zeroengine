# ZeroEngine.Narrative

Narrative systems for story-driven games.

## Modules

- **Dialog** - Node-based dialogue system with branching and conditions
- **Quest** - Quest management with objectives and rewards
- **Achievement compatibility** - Legacy rewards, slot-save adapter and type forwarding to the standalone achievement package

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support
- `com.zerogamestudio.zeroengine.achievement` - Standalone achievement state and provider contracts
- `com.zerogamestudio.zeroengine.data`, `com.zerogamestudio.zeroengine.economy`, `com.zerogamestudio.zeroengine.gameplay` - Existing legacy condition/reward adapters

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## 3.1.0 candidate convergence

Combines the published 3.0.0 achievement extraction with the consumer-tested 2.1.0 dialog commands, graph-provider commands, quest snapshots and runtime services. These P5-origin generic contracts and their tests remain in this package.

Achievement definitions keep their script GUIDs and legacy assembly-qualified type names forward to `ZeroEngine.Achievement`. Legacy gameplay rewards and the explicit slot-save bridge stay here. Steam achievement integration is an optional separate package; P5 has no direct achievement or Steam achievement provider consumer in this scope. P5's project-owned save contract is not migrated.
