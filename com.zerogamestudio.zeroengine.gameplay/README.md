# ZeroEngine.Gameplay

General gameplay systems.

## Modules

- **Interaction** - Interactable objects with conditions
- **Tutorial** - Tutorial sequence system
- **Command** - Command pattern with undo/redo

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Request-scoped relationship conditions (2.1 candidate)

Pass `IInteractionRelationshipQuery` in the interaction context for the requesting
actor. `RelationshipCondition` requires that query to resolve the configured stable
NPC id; missing/unknown facts and invalid thresholds refuse the action. It has no
Social package reference, compile-symbol shortcut or global relationship fallback.
The existing three-argument context constructor remains available for interactions
that do not use relationship conditions. Consumer composition supplies its domain
query adapter and localization for failure presentation.

### Explicit reward recipients

`RewardRequest` carries optional stable `RecipientId` independently of `SourceId` and `PayloadId`. The original five-argument constructor remains available to compiled consumers and delegates to a separate six-argument recipient overload. Existing calls retain an empty recipient; shared inventory or party-wide rewards can keep that meaning. An actor-specific consumer validates the explicit recipient against its current roster and request context, refusing missing, ambiguous or obsolete targets. Recipient selection, project definitions and persistent one-shot ledgers stay with consumers.

## Tutorial guidance (2.1.0)


Scoped UI/world targets, multi-target masks, native click/hold/drag, context signals and CG/video/Timeline presentation are documented in [Tutorial](Runtime/Tutorial/README.md). Import Guidance Playground from Samples for an independent consumer example.

## Build tags and reward selection (2.2)

Project data defines stable IDs through BuildTagCatalog. Construct a BuildTagSnapshot
from the requesting player's committed choices. Each BuildTagSource supplies a
stable source ID and its tags. Duplicate source IDs contribute at most once. Call
GetMultiplier with the candidate's tags, the per-source bonus and the cap. The
default is 1x, 1.5x, or up to 2x. This scoring has no inventory or RNG dependency.

RewardSelection accepts eligible candidates in stable order. SelectDistinct draws
without replacement using the caller's random stream. TrySelectTiered lets the
caller apply ZeroEngine.Core's WeightedSelection.LuckMultiplier to tier weights
before drawing an item inside that tier. TryRedistributeTierWeights keeps the
existing tier totals for a consumer that draws from a single combined pool.
Projects own eligibility, content tags, random streams, player state and tuning.
ZeroEngine.Core 2.2.2 or newer supplies the shared weighted draw and luck curve.
