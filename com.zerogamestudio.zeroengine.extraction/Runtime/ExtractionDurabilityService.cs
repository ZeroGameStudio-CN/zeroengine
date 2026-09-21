using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionEquipmentWearKind { WeaponAction, ReceivedDamage }

    public static class ExtractionDurabilityService
    {
        public static bool MatchesWeaponSlot(string equippedSlot, string actionSlot) =>
            equippedSlot == actionSlot || equippedSlot == "weapon" && actionSlot == "weapon-primary";

        public static bool IsBroken(ExtractionItemDefinition definition, ExtractionItemInstance item) =>
            definition != null && item != null && definition.MaxDurability > 0 && item.CurrentDurability <= 0;

        // Deterministic per operation/item: retrying a failed commit cannot reroll wear probability.
        public static bool ShouldWear(ExtractionItemDefinition definition, string operationId, string itemId)
        {
            if (definition == null || definition.MaxDurability <= 0 || definition.DurabilityReductionAmount <= 0
                || definition.DurabilityReductionProbability <= 0f) return false;
            if (definition.DurabilityReductionProbability >= 1f) return true;
            uint hash = 2166136261;
            foreach (char c in (operationId ?? "") + ":" + (itemId ?? "")) hash = unchecked((hash ^ c) * 16777619);
            return hash / 4294967296d < definition.DurabilityReductionProbability;
        }

        // Operates on a staged profile; the caller owns atomic persistence and projection.
        public static bool TryWearEquipment(ExtractionProfileSaveData profile, ExtractionRaidInventoryState raid,
            IExtractionItemCatalog catalog, ExtractionEquipmentWearKind kind, string operationId,
            string weaponSlotId, out int changed)
        {
            changed = 0;
            if (profile == null || raid == null || catalog == null || string.IsNullOrEmpty(operationId)) return false;
            profile.EnsureInitialized();
            raid.EnsureInitialized();
            if (raid.WearReceiptIds.Contains(operationId)) return true;
            foreach (var slot in new List<ExtractionEquipmentSlotState>(raid.Equipment.Slots))
            {
                if (slot == null || !profile.Items.TryGet(slot.ItemInstanceId, out var item)
                    || !catalog.TryGetItemDefinition(item.DefinitionId, out var definition)) return false;
                var type = ExtractionItemActionPolicyService.GetPolicy(definition).EquipmentSlotType;
                bool eligible = kind == ExtractionEquipmentWearKind.WeaponAction
                    ? type == ExtractionEquipmentSlotType.Weapon && MatchesWeaponSlot(slot.SlotId, weaponSlotId)
                    : (int)type >= (int)ExtractionEquipmentSlotType.Head && (int)type <= (int)ExtractionEquipmentSlotType.Gloves;
                if (!eligible || IsBroken(definition, item) || !ShouldWear(definition, operationId, item.InstanceId)) continue;
                item.CurrentDurability = Math.Max(0, item.CurrentDurability - definition.DurabilityReductionAmount);
                changed++;
                if (item.CurrentDurability != 0 || !definition.DestroyOnZeroDurability) continue;
                if (!raid.Equipment.TryClear(slot.SlotId, item.InstanceId)
                    || !profile.Ownership.TryMove(item.InstanceId, ExtractionInventoryContainerType.EquipmentSlot,
                        ExtractionInventoryContainerType.DestroyedByUse, "durability", null)) return false;
            }
            if (changed > 0)
            {
                raid.WearReceiptIds.Add(operationId);
                if (raid.WearReceiptIds.Count > 256) raid.WearReceiptIds.RemoveAt(0);
            }
            return true;
        }
    }
}
