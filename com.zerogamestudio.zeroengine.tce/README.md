# ZeroEngine TCE

ZeroEngine TCE provides a generic Trigger -> Condition -> Effect runtime for gameplay rules.

## Boundary

Runtime code in this package must not reference:

- `POB.*`
- Odin / Sirenix
- DOTween
- Unity Input System
- MoreMountains Feedbacks
- PixelCrushers
- weapon, projectile, buff, room, inventory, or player-specific game domains

Game-specific projects should connect to the package through adapter assemblies.

This first extraction slice is synchronous. Delayed trigger scheduling belongs in a later scheduler adapter after the owning game can route callbacks through its gameplay clock.

## First Generic Components

- `OnInstallTrigger`
- `NumericSourceCondition`
- `CooldownCondition`
- `DebugLogEffect`

These are intentionally small because they are useful across games and do not assume a POB combat model.
