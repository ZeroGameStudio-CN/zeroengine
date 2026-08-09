using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemCost
    {
        public string ItemDefinitionId;
        public int Quantity;

        public bool IsValid => !string.IsNullOrEmpty(ItemDefinitionId) && Quantity > 0;

        public ExtractionItemCost(string itemDefinitionId, int quantity)
        {
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
        }
    }

    [Serializable]
    public class ExtractionConsumedCostEntry
    {
        public string ItemInstanceId;
        public string DefinitionId;
        public ExtractionInventoryContainerType Container;
        public int RemovedQuantity;
        public bool WasFullyRemoved;
        public string SourceKind;
        public string SourceId;
        public int X;
        public int Y;
        public bool Rotated;
    }

    public class ExtractionCostConsumptionReceipt
    {
        public List<ExtractionConsumedCostEntry> Entries = new();
    }

    public static class ExtractionItemCostService
    {
        public static int GetAvailableQuantity(
            ExtractionProfileSaveData profile,
            string itemDefinitionId,
            List<ExtractionInventoryContainerType> containers)
        {
            if (!ExtractionFeatureSwitch.Enabled) return 0;
            if (profile == null || string.IsNullOrEmpty(itemDefinitionId) || containers == null) return 0;
            profile.EnsureInitialized();

            long total = 0;
            foreach (var container in containers)
            {
                if (!TryGetGrid(profile, container, out var grid)) continue;

                foreach (var placement in grid.Placements)
                {
                    if (placement == null) continue;
                    if (!profile.Items.TryGet(placement.ItemInstanceId, out var item)) continue;
                    if (item.DefinitionId != itemDefinitionId || item.Quantity <= 0) continue;
                    if (!profile.Ownership.TryGetContainer(placement.ItemInstanceId, out var current)) continue;
                    if (current != container) continue;

                    total += item.Quantity;
                }
            }

            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public static bool HasEnoughItems(
            ExtractionProfileSaveData profile,
            List<ExtractionItemCost> costs,
            List<ExtractionInventoryContainerType> containers)
        {
            if (!TryBuildRequiredQuantities(costs, out var required)) return false;

            foreach (var entry in required)
            {
                if (GetAvailableQuantity(profile, entry.Key, containers) < entry.Value)
                    return false;
            }

            return true;
        }

        public static bool TryConsumeCosts(
            ExtractionProfileSaveData profile,
            List<ExtractionItemCost> costs,
            List<ExtractionInventoryContainerType> containers)
        {
            return TryConsumeCosts(profile, costs, containers, out _);
        }

        public static bool TryConsumeCosts(
            ExtractionProfileSaveData profile,
            List<ExtractionItemCost> costs,
            List<ExtractionInventoryContainerType> containers,
            out ExtractionCostConsumptionReceipt receipt)
        {
            receipt = new ExtractionCostConsumptionReceipt();
            if (profile == null || containers == null) return false;
            if (!TryBuildRequiredQuantities(costs, out var required)) return false;
            if (!HasEnoughItems(profile, costs, containers)) return false;

            profile.EnsureInitialized();
            foreach (var entry in required)
            {
                if (!TryConsumeQuantity(profile, entry.Key, entry.Value, containers, receipt))
                    return false;
            }

            return true;
        }

        // 把回执逐项逆操作：存活项加回数量、整项消费的按原 placement 重建。
        public static bool RestoreCosts(
            ExtractionProfileSaveData profile,
            ExtractionCostConsumptionReceipt receipt,
            IExtractionItemCatalog itemCatalog)
        {
            if (profile == null || receipt == null) return false;
            profile.EnsureInitialized();

            bool allRestored = true;
            for (int i = receipt.Entries.Count - 1; i >= 0; i--)
            {
                var entry = receipt.Entries[i];
                if (entry == null) continue;

                if (!entry.WasFullyRemoved
                    && profile.Items.TryGet(entry.ItemInstanceId, out var survivor))
                {
                    survivor.Quantity += entry.RemovedQuantity;
                    continue;
                }

                // 还原失败属不变量违反（格子刚腾出、容量必然够）：记录并尽力还原其余项，不再吞物。
                if (!TryRecreateConsumedItem(profile, entry, itemCatalog))
                {
                    UnityEngine.Debug.LogError(
                        $"[Extraction] RestoreCosts 无法重建已消耗成本物 {entry.ItemInstanceId} ({entry.DefinitionId})。");
                    allRestored = false;
                }
            }

            return allRestored;
        }

        private static bool TryRecreateConsumedItem(
            ExtractionProfileSaveData profile,
            ExtractionConsumedCostEntry entry,
            IExtractionItemCatalog itemCatalog)
        {
            if (itemCatalog == null) return false;
            if (!itemCatalog.TryGetItemDefinition(entry.DefinitionId, out var definition)) return false;
            if (!TryGetGrid(profile, entry.Container, out var grid)) return false;

            var item = new ExtractionItemInstance(
                entry.ItemInstanceId,
                entry.DefinitionId,
                entry.RemovedQuantity,
                entry.SourceKind,
                entry.SourceId);
            if (!profile.Items.Register(item)) return false;
            if (!grid.TryPlace(item, definition, entry.X, entry.Y, entry.Rotated))
            {
                profile.Items.TryRemove(entry.ItemInstanceId);
                return false;
            }

            if (!profile.Ownership.Register(entry.ItemInstanceId, entry.Container))
            {
                grid.TryRemove(entry.ItemInstanceId);
                profile.Items.TryRemove(entry.ItemInstanceId);
                return false;
            }

            return true;
        }

        private static bool TryBuildRequiredQuantities(
            List<ExtractionItemCost> costs,
            out Dictionary<string, int> required)
        {
            required = new Dictionary<string, int>();
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (costs == null || costs.Count == 0) return false;

            var acc = new Dictionary<string, long>();
            foreach (var cost in costs)
            {
                if (cost == null || !cost.IsValid) return false;

                acc.TryGetValue(cost.ItemDefinitionId, out long current);
                long sum = current + cost.Quantity;
                acc[cost.ItemDefinitionId] = sum > int.MaxValue ? int.MaxValue : sum;
            }

            foreach (var kv in acc)
                required[kv.Key] = (int)kv.Value;

            return required.Count > 0;
        }

        private static bool TryConsumeQuantity(
            ExtractionProfileSaveData profile,
            string itemDefinitionId,
            int quantity,
            List<ExtractionInventoryContainerType> containers,
            ExtractionCostConsumptionReceipt receipt)
        {
            int remaining = quantity;
            foreach (var container in containers)
            {
                if (!TryGetGrid(profile, container, out var grid)) continue;
                var placements = new List<ExtractionItemPlacement>(grid.Placements);

                foreach (var placement in placements)
                {
                    if (placement == null) continue;
                    if (!profile.Items.TryGet(placement.ItemInstanceId, out var item)) continue;
                    if (item.DefinitionId != itemDefinitionId || item.Quantity <= 0) continue;
                    if (!profile.Ownership.TryGetContainer(placement.ItemInstanceId, out var current)) continue;
                    if (current != container) continue;

                    int consumed = Math.Min(remaining, item.Quantity);
                    receipt.Entries.Add(new ExtractionConsumedCostEntry
                    {
                        ItemInstanceId = item.InstanceId,
                        DefinitionId = item.DefinitionId,
                        Container = container,
                        RemovedQuantity = consumed,
                        WasFullyRemoved = consumed >= item.Quantity,
                        SourceKind = item.SourceKind,
                        SourceId = item.SourceId,
                        X = placement.X,
                        Y = placement.Y,
                        Rotated = placement.Rotated
                    });
                    item.Quantity -= consumed;
                    remaining -= consumed;

                    if (item.Quantity <= 0)
                    {
                        grid.TryRemove(item.InstanceId);
                        profile.Items.TryRemove(item.InstanceId);
                        profile.Ownership.TryRemove(item.InstanceId);
                    }

                    if (remaining <= 0) return true;
                }
            }

            return false;
        }

        private static bool TryGetGrid(
            ExtractionProfileSaveData profile,
            ExtractionInventoryContainerType container,
            out ExtractionItemGrid grid)
        {
            grid = container switch
            {
                ExtractionInventoryContainerType.Stash => profile.Stash,
                ExtractionInventoryContainerType.Loadout => profile.Loadout,
                ExtractionInventoryContainerType.SecureContainer => profile.SecureContainer,
                ExtractionInventoryContainerType.Holding => profile.RecoveryHolding,
                _ => null
            };

            return grid != null;
        }
    }
}
