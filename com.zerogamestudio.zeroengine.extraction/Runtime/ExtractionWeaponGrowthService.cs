using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionWeaponGrowthDefinition
    {
        public string ItemDefinitionId;
        public List<ExtractionWeaponGrowthStepDefinition> EnhancementSteps = new();
        public List<ExtractionWeaponGrowthStepDefinition> ForgeSteps = new();
        public List<ExtractionWeaponAffixDefinition> Affixes = new();
    }

    [Serializable]
    public class ExtractionWeaponGrowthStepDefinition
    {
        public int TargetLevel;
        public List<ExtractionItemCost> Costs = new();

        public ExtractionWeaponGrowthStepDefinition(int targetLevel)
        {
            TargetLevel = targetLevel;
        }
    }

    [Serializable]
    public class ExtractionWeaponAffixDefinition
    {
        public string AffixId;
        public List<ExtractionItemCost> Costs = new();

        public ExtractionWeaponAffixDefinition(string affixId)
        {
            AffixId = affixId;
        }
    }

    public enum ExtractionWeaponGrowthResult
    {
        Succeeded = 0,
        AlreadyApplied = 1,
        Cancelled = 2,
        InvalidRequest = 3,
        WeaponNotFound = 4,
        NotAWeapon = 5,
        StepUnavailable = 6,
        InsufficientMaterials = 7,
        InvalidAffix = 8,
        CommitFailed = 9
    }

    public static class ExtractionWeaponGrowthService
    {
        public static bool TryEnhance(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            ExtractionWeaponGrowthDefinition growth,
            string weaponItemInstanceId,
            List<ExtractionInventoryContainerType> materialContainers,
            string receiptId,
            out ExtractionWeaponGrowthResult result)
        {
            return TryApplyStep(
                profile,
                itemCatalog,
                growth,
                weaponItemInstanceId,
                materialContainers,
                receiptId,
                isForge: false,
                out result);
        }

        public static bool TryForge(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            ExtractionWeaponGrowthDefinition growth,
            string weaponItemInstanceId,
            List<ExtractionInventoryContainerType> materialContainers,
            string receiptId,
            out ExtractionWeaponGrowthResult result)
        {
            return TryApplyStep(
                profile,
                itemCatalog,
                growth,
                weaponItemInstanceId,
                materialContainers,
                receiptId,
                isForge: true,
                out result);
        }

        public static bool TryReplaceAffix(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            ExtractionWeaponGrowthDefinition growth,
            string weaponItemInstanceId,
            int affixIndex,
            string newAffixId,
            bool confirmed,
            List<ExtractionInventoryContainerType> materialContainers,
            string receiptId,
            out ExtractionWeaponGrowthResult result)
        {
            result = confirmed
                ? ExtractionWeaponGrowthResult.InvalidRequest
                : ExtractionWeaponGrowthResult.Cancelled;
            if (!confirmed) return false;
            if (!TryGetWeapon(
                    profile,
                    itemCatalog,
                    growth,
                    weaponItemInstanceId,
                    receiptId,
                    out var weapon,
                    out result))
            {
                return result == ExtractionWeaponGrowthResult.AlreadyApplied;
            }
            if (affixIndex < 0 || affixIndex > weapon.AffixIds.Count)
            {
                result = ExtractionWeaponGrowthResult.InvalidAffix;
                return false;
            }
            if (!TryGetAffix(growth, newAffixId, out var affix))
            {
                result = ExtractionWeaponGrowthResult.InvalidAffix;
                return false;
            }
            if (!TryConsume(profile, itemCatalog, affix.Costs, materialContainers, out var receipt))
            {
                result = ExtractionWeaponGrowthResult.InsufficientMaterials;
                return false;
            }

            try
            {
                if (affixIndex == weapon.AffixIds.Count) weapon.AffixIds.Add(newAffixId);
                else weapon.AffixIds[affixIndex] = newAffixId;
                profile.ItemActionReceiptIds.Add(receiptId);
            }
            catch
            {
                ExtractionItemCostService.RestoreCosts(profile, receipt, itemCatalog);
                throw;
            }

            result = ExtractionWeaponGrowthResult.Succeeded;
            return true;
        }

        private static bool TryApplyStep(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            ExtractionWeaponGrowthDefinition growth,
            string weaponItemInstanceId,
            List<ExtractionInventoryContainerType> materialContainers,
            string receiptId,
            bool isForge,
            out ExtractionWeaponGrowthResult result)
        {
            if (!TryGetWeapon(
                    profile,
                    itemCatalog,
                    growth,
                    weaponItemInstanceId,
                    receiptId,
                    out var weapon,
                    out result))
            {
                return result == ExtractionWeaponGrowthResult.AlreadyApplied;
            }

            int current = isForge ? weapon.ForgeTier : weapon.EnhancementLevel;
            var steps = isForge ? growth.ForgeSteps : growth.EnhancementSteps;
            if (!TryGetStep(steps, current + 1, out var step))
            {
                result = ExtractionWeaponGrowthResult.StepUnavailable;
                return false;
            }
            if (!TryConsume(profile, itemCatalog, step.Costs, materialContainers, out var receipt))
            {
                result = ExtractionWeaponGrowthResult.InsufficientMaterials;
                return false;
            }

            if (isForge) weapon.ForgeTier = step.TargetLevel;
            else weapon.EnhancementLevel = step.TargetLevel;
            profile.ItemActionReceiptIds.Add(receiptId);
            result = ExtractionWeaponGrowthResult.Succeeded;
            return true;
        }

        private static bool TryGetWeapon(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            ExtractionWeaponGrowthDefinition growth,
            string weaponItemInstanceId,
            string receiptId,
            out ExtractionItemInstance weapon,
            out ExtractionWeaponGrowthResult result)
        {
            weapon = null;
            result = ExtractionWeaponGrowthResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile == null
                || itemCatalog == null
                || growth == null
                || string.IsNullOrEmpty(growth.ItemDefinitionId)
                || string.IsNullOrEmpty(weaponItemInstanceId)
                || string.IsNullOrEmpty(receiptId)
                || materialListsInvalid(growth))
            {
                return false;
            }

            profile.EnsureInitialized();
            if (profile.ItemActionReceiptIds.Contains(receiptId))
            {
                result = ExtractionWeaponGrowthResult.AlreadyApplied;
                return false;
            }
            if (!profile.Items.TryGet(weaponItemInstanceId, out weapon)
                || weapon.DefinitionId != growth.ItemDefinitionId
                || !itemCatalog.TryGetItemDefinition(weapon.DefinitionId, out var definition))
            {
                result = ExtractionWeaponGrowthResult.WeaponNotFound;
                return false;
            }

            if (!ExtractionItemActionPolicyService.CanEquip(definition)
                || definition.ActionPolicy.EquipmentSlotType != ExtractionEquipmentSlotType.Weapon)
            {
                result = ExtractionWeaponGrowthResult.NotAWeapon;
                return false;
            }

            return true;
        }

        private static bool TryConsume(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionItemCost> costs,
            List<ExtractionInventoryContainerType> containers,
            out ExtractionCostConsumptionReceipt receipt)
        {
            receipt = null;
            if (costs == null || costs.Count == 0) return false;
            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, containers)) return false;
            return ExtractionItemCostService.TryConsumeCosts(profile, costs, containers, out receipt);
        }

        private static bool TryGetStep(
            List<ExtractionWeaponGrowthStepDefinition> steps,
            int targetLevel,
            out ExtractionWeaponGrowthStepDefinition step)
        {
            step = null;
            if (steps == null) return false;
            foreach (var candidate in steps)
            {
                if (candidate?.TargetLevel != targetLevel) continue;
                if (step != null) return false;
                step = candidate;
            }
            return step != null;
        }

        private static bool TryGetAffix(
            ExtractionWeaponGrowthDefinition growth,
            string affixId,
            out ExtractionWeaponAffixDefinition affix)
        {
            affix = null;
            if (growth?.Affixes == null || string.IsNullOrEmpty(affixId)) return false;
            foreach (var candidate in growth.Affixes)
            {
                if (candidate?.AffixId != affixId) continue;
                if (affix != null) return false;
                affix = candidate;
            }
            return affix != null;
        }

        private static bool materialListsInvalid(ExtractionWeaponGrowthDefinition growth)
        {
            return growth.EnhancementSteps == null || growth.ForgeSteps == null || growth.Affixes == null;
        }
    }
}
