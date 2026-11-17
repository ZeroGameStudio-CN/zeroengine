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
- Unity's JSON serialization module - Authored reward manifest hashing

## Quest 3.2.0

### Authoring and activation

QuestConfigSO keeps the existing Conditions, AcceptRequirements and Rewards
lists. Set autoActivate for a system/unlock quest, repetitionLimit to 1, and
autoSubmit if its reward needs no hand-in. Persistent means once per save;
PerRun means once per run after ClearPerRunQuests clears active per-run state
and records a new completion-count baseline. Lifetime history is never erased.
Old saves conservatively use their lifetime counts for limits until the first
explicit run reset; upgrading cannot immediately re-award an already completed
per-run quest. Quest-state prerequisites retain their existing active/lifetime
semantics; the per-run baseline controls repetition limits.

After configuration and the intended save slot are loaded, the consumer calls:

    manager.LoadSaveData(savedState);
    manager.RegisterConfigs(configs);
    manager.ActivateConfiguredQuests();

Activation is explicit and needs no HUD, NPC or quest board. Calling it again
is safe. Newly registered automatic definitions and satisfied quest prerequisites
are evaluated after activation. LoadSaveData suspends activation so loading a
different slot cannot award content before its owner finishes setup. The owner
calls ActivateConfiguredQuests again at that boundary and after a run reset.
The same call retries pending auto-submit claims once per instance per activation
pass, resuming saved receipts without spinning on a failed reward service.
It also reconciles loaded Active quests whose saved counters already meet their
conditions, so recovery does not require killing the same target a second time.
Unlimited automatic repetition is refused to prevent self-completing loops.

Use the quest asset's context menu, Assign Missing Condition and Reward IDs,
before publishing new content. This is an explicit Undo-compatible edit, not an
import-time rewrite of consumer assets. Display/localization keys remain
compatible with GetProgressKey; built-in progress storage uses GetStorageKey.
Editor validation reports duplicate quest IDs, conflicting progress identities,
invalid counts, null entries, duplicate rewards and unsafe activation settings.
NPC availability uses the same CanAcceptQuest check as actual acceptance.

### Recoverable reward delivery

TrySubmitQuest returns a QuestSubmissionResult with a status and the failed
reward identity. The legacy void SubmitQuest API remains a wrapper. Failed or
throwing grants leave the quest Successful and claimable. Confirmed reward
receipts are retained so retrying does not replay earlier successful items.
Reentrant submission cannot grant twice. A repeatable quest receives a fresh
instance identity for each acceptance.

Existing IQuestRewardService implementations remain supported. For durable
external grants implement IContextualQuestRewardService; its context includes
QuestId, InstanceId, RewardId and ReceiptId. The recipient must persist ReceiptId
with the side effect, treating a repeated receipt as an already confirmed grant.
OnRewardGranted allows the save owner to persist a quest snapshot with recipient
state after each confirmed item. GetSaveData and ExportSaveData return detached
snapshots. The serializer must preserve the public lists and dictionaries.

The package cannot atomically commit an unrelated inventory/backend. Exactly-once
recovery across a crash between an external grant and saving the quest receipt
requires the contextual service's durable receipt or a consumer-owned transaction.
A legacy boolean service protects saved quest receipts but cannot by itself
close that external crash window.

Pending claims freeze the authored reward manifest. Stable reward IDs allow
reordering; changed payloads, removed rewards or shifted legacy list positions
require an explicit receipt migration instead of silently granting new content.
Do not mutate serialized reward fields as runtime counters.
Custom rewards with Unity object references override GetGrantFingerprint using
stable catalog IDs and all grant-affecting parameters. ItemReward already uses
the referenced item ID, so asset instance IDs changing on reload do not block a
pending claim. Extend that fingerprint when adding grant parameters in a subclass.

### Compatibility

Quest save schema 1 accepts schema 0 and adds instance/receipt fields without
changing existing state or lifecycle enum values. Missing legacy instance IDs
are assigned in the loaded copy; source payloads are preserved. Newer schemas,
duplicate state records and corrupt progress are rejected before replacing
the active state.

Blank condition IDs retain legacy keys. Assigning an ID migrates an unambiguous
old counter; ambiguous or conflicting counters are rejected for explicit repair.
Unknown quest definitions are retained in snapshots for later content loading.

Legacy expReward/goldReward/itemRewards fields are retained for reading and an
explicit Migrate Legacy Rewards context-menu action. Legacy completion/failure
fields require explicit Conditions authoring. Validation and runtime acceptance
refuse unmigrated fields; previews list executable Rewards only. Migration does
not happen merely because an asset is loaded.

Game-specific reward subclasses (such as unlocking a game's keepsake) remain
in consumer adapters. This package owns activation, condition progression and
reliable reward dispatch; it does not enable any consumer's task gameplay.

## Release history

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## 3.1.0 candidate convergence

Combines the published 3.0.0 achievement extraction with the consumer-tested 2.1.0 dialog commands, graph-provider commands, quest snapshots and runtime services. These P5-origin generic contracts and their tests remain in this package.

Achievement definitions keep their script GUIDs and legacy assembly-qualified type names forward to `ZeroEngine.Achievement`. Legacy gameplay rewards and the explicit slot-save bridge stay here. Steam achievement integration is an optional separate package; P5 has no direct achievement or Steam achievement provider consumer in this scope. P5's project-owned save contract is not migrated.
