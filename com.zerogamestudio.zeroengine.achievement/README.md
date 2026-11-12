# ZeroEngine.Achievement

Standalone achievement definitions, event and counter conditions, progress, and
provider contracts. Install it with ZeroEngine.Core from a matching tested Git
revision. The package has no direct dependency on Narrative, Persistence, Economy,
or Steamworks.NET.

Create a definition using **Create → ZeroEngine → Achievement → Achievement**.
Give it a stable ID and add conditions. Assign it to an AchievementManager or call
RegisterAchievement. Trigger an event by its ID and listen for OnAchievementUnlocked.

The host owns persistence: export a progress snapshot with ExportSaveData and load it
with ImportSaveData. The slot key and serialized fields are unchanged. The default
manager does not create a save store or a platform provider. Hosts that already
save achievements keep their own save bridge. Narrative users can bind the explicit
AchievementSaveSlotAdapter to their existing store before a slot loads.

Register external providers explicitly with RegisterProvider. AchievementUploadQueue
deduplicates pending IDs and serializes writes with reset operations. The host owns
account identity and confirmation and removes its durable intent only after a write
is confirmed. Use the queue from one owning thread.

The original MonoScript GUIDs are preserved. MovedFrom handles managed references
from the old assembly. Keep Narrative installed for CLR type forwarding when a
legacy save stores a ZeroEngine.Narrative assembly-qualified type name. Legacy
StateCondition and inventory-dependent reward types remain in Narrative. Existing
authoring window and dashboard entry remain in the ZeroEngine umbrella Editor
package. See the repository consumer setup guide for the manifest migration.
