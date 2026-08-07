using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionEquipmentSlotType
    {
        None = 0,
        Weapon = 1,
        Relic = 2,
        Card = 3
    }

    public enum ExtractionItemConsumptionType
    {
        None = 0,
        Quantity = 1,
        Durability = 2,
        DestroyInstance = 3
    }

    public enum ExtractionItemActionPreset
    {
        NormalLoot = 0,
        Equippable = 1,
        Consumable = 2,
        QuestKeepOnDeath = 3,
        QuestDropOnDeath = 4,
        DurableTool = 5
    }

    [Serializable]
    public class ExtractionItemActionPolicy
    {
        public bool PolicyInitialized;
        public bool CanEquip;
        public ExtractionEquipmentSlotType EquipmentSlotType;
        public string EffectAdapterId;
        public bool CanUse;
        public string UseActionId;
        public ExtractionItemConsumptionType ConsumptionType;
        public bool CanDrop = true;
        public bool CanSell = true;
        public bool DropOnDeath = true;
        public bool CanPlaceInSecure = true;
        public bool RaidBound;
        public bool DestroyOnSettlement;

        public static ExtractionItemActionPolicy CreateDefaultLoot()
        {
            return new ExtractionItemActionPolicy
            {
                PolicyInitialized = true,
                CanDrop = true,
                CanSell = true,
                DropOnDeath = true,
                CanPlaceInSecure = true
            };
        }

        internal void EnsureInitialized()
        {
            if (PolicyInitialized) return;
            CanDrop = true;
            CanSell = true;
            DropOnDeath = true;
            CanPlaceInSecure = true;
            PolicyInitialized = true;
        }
    }

    public static class ExtractionItemActionPresetService
    {
        private static readonly Dictionary<ExtractionItemActionPreset, string> ChineseLabels = new()
        {
            [ExtractionItemActionPreset.NormalLoot] = "普通物品",
            [ExtractionItemActionPreset.Equippable] = "可装备物品",
            [ExtractionItemActionPreset.Consumable] = "可使用消耗品",
            [ExtractionItemActionPreset.QuestKeepOnDeath] = "任务物品（死亡保留）",
            [ExtractionItemActionPreset.QuestDropOnDeath] = "任务物品（死亡掉落）",
            [ExtractionItemActionPreset.DurableTool] = "耐久工具 / 钥匙"
        };

        private static readonly Dictionary<ExtractionItemActionPreset, string> ChineseTooltips = new()
        {
            [ExtractionItemActionPreset.NormalLoot] = "默认可丢弃、可出售、死亡掉落并可放入保险箱。",
            [ExtractionItemActionPreset.Equippable] = "在普通物品默认值上开启装备；仍需选择装备位类型和效果适配器。",
            [ExtractionItemActionPreset.Consumable] = "在普通物品默认值上开启使用并按数量消耗；仍需填写使用行为。",
            [ExtractionItemActionPreset.QuestKeepOnDeath] = "不能主动丢弃或出售，死亡时保留；任务系统仍可提交。",
            [ExtractionItemActionPreset.QuestDropOnDeath] = "不能主动丢弃或出售，但死亡时进入掉落 / 尸体流程；任务系统仍可提交。",
            [ExtractionItemActionPreset.DurableTool] = "按实例耐久使用；仍需填写使用行为、最大耐久和兼容目标。"
        };

        public static bool TryApply(
            ExtractionItemDefinition definition,
            ExtractionItemActionPreset preset)
        {
            if (definition == null || !Enum.IsDefined(typeof(ExtractionItemActionPreset), preset))
                return false;

            var policy = ExtractionItemActionPolicy.CreateDefaultLoot();
            switch (preset)
            {
                case ExtractionItemActionPreset.NormalLoot:
                    break;
                case ExtractionItemActionPreset.Equippable:
                    policy.CanEquip = true;
                    break;
                case ExtractionItemActionPreset.Consumable:
                    policy.CanUse = true;
                    policy.ConsumptionType = ExtractionItemConsumptionType.Quantity;
                    break;
                case ExtractionItemActionPreset.QuestKeepOnDeath:
                    policy.CanDrop = false;
                    policy.CanSell = false;
                    policy.DropOnDeath = false;
                    break;
                case ExtractionItemActionPreset.QuestDropOnDeath:
                    policy.CanDrop = false;
                    policy.CanSell = false;
                    policy.DropOnDeath = true;
                    break;
                case ExtractionItemActionPreset.DurableTool:
                    policy.CanUse = true;
                    policy.ConsumptionType = ExtractionItemConsumptionType.Durability;
                    break;
                default:
                    return false;
            }

            definition.ActionPolicy = policy;
            return true;
        }

        public static string GetChineseLabel(ExtractionItemActionPreset preset)
        {
            return ChineseLabels.TryGetValue(preset, out string label) ? label : preset.ToString();
        }

        public static string GetChineseTooltip(ExtractionItemActionPreset preset)
        {
            return ChineseTooltips.TryGetValue(preset, out string tooltip) ? tooltip : string.Empty;
        }
    }

    public static class ExtractionItemActionPolicyService
    {
        public static ExtractionItemActionPolicy GetPolicy(ExtractionItemDefinition definition)
        {
            if (definition == null) return null;
            definition.ActionPolicy ??= ExtractionItemActionPolicy.CreateDefaultLoot();
            definition.ActionPolicy.EnsureInitialized();
            return definition.ActionPolicy;
        }

        public static bool CanSubmitToTask(ExtractionItemDefinition definition)
        {
            return definition != null;
        }

        public static bool CanEquip(ExtractionItemDefinition definition)
        {
            var policy = GetPolicy(definition);
            return policy != null
                   && policy.CanEquip
                   && policy.EquipmentSlotType != ExtractionEquipmentSlotType.None
                   && !string.IsNullOrWhiteSpace(policy.EffectAdapterId);
        }

        public static bool CanUse(ExtractionItemDefinition definition)
        {
            var policy = GetPolicy(definition);
            return policy != null
                   && policy.CanUse
                   && !string.IsNullOrWhiteSpace(policy.UseActionId)
                   && policy.ConsumptionType != ExtractionItemConsumptionType.None;
        }

        public static bool CanDrop(ExtractionItemDefinition definition, ExtractionItemInstance item = null)
        {
            var policy = GetPolicy(definition);
            return policy != null
                   && policy.CanDrop
                   && (item == null || item.HasFlag(ExtractionItemInstanceFlags.CanDrop));
        }

        public static bool CanSell(ExtractionItemDefinition definition, ExtractionItemInstance item = null)
        {
            var policy = GetPolicy(definition);
            return policy != null
                   && policy.CanSell
                   && (item == null || item.HasFlag(ExtractionItemInstanceFlags.CanSell));
        }

        public static bool DropsOnDeath(ExtractionItemDefinition definition, ExtractionItemInstance item = null)
        {
            var policy = GetPolicy(definition);
            return policy != null
                   && policy.DropOnDeath
                   && (item == null || item.HasFlag(ExtractionItemInstanceFlags.DropOnDeath));
        }

        public static bool CanPlaceInSecure(ExtractionItemDefinition definition)
        {
            return GetPolicy(definition)?.CanPlaceInSecure == true;
        }

        public static void ApplyDefinitionPolicyToInstance(
            ExtractionItemDefinition definition,
            ExtractionItemInstance item)
        {
            if (definition == null || item == null) return;
            var policy = GetPolicy(definition);
            if (policy == null) return;

            item.Flags = ExtractionItemInstanceFlags.PolicyInitialized;
            SetFlag(item, ExtractionItemInstanceFlags.CanDrop, policy.CanDrop);
            SetFlag(item, ExtractionItemInstanceFlags.CanSell, policy.CanSell);
            SetFlag(item, ExtractionItemInstanceFlags.DropOnDeath, policy.DropOnDeath);
            SetFlag(item, ExtractionItemInstanceFlags.RaidBound, policy.RaidBound);
            SetFlag(item, ExtractionItemInstanceFlags.DestroyOnSettlement, policy.DestroyOnSettlement);
            if (definition.MaxDurability > 0 && item.CurrentDurability <= 0)
                item.CurrentDurability = definition.MaxDurability;
        }

        private static void SetFlag(
            ExtractionItemInstance item,
            ExtractionItemInstanceFlags flag,
            bool enabled)
        {
            if (enabled) item.Flags |= flag;
            else item.Flags &= ~flag;
        }
    }
}
