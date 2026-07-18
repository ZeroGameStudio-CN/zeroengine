using System.Collections.Generic;

namespace POB.Extraction
{
    public class ExtractionPlayableConfig : IExtractionItemCatalog, IExtractionLootTableCatalog
    {
        public int RaidBackpackWidth;
        public int RaidBackpackHeight;
        public int SecureContainerWidth;
        public int SecureContainerHeight;
        public ExtractionCharacterConfig Character = new();
        public List<ExtractionMapDefinition> Maps = new();
        public List<ExtractionPointDefinition> ExtractionPoints = new();
        public List<ExtractionItemDefinition> ItemDefinitions = new();
        public List<ExtractionLootTableDefinition> LootTables = new();
        public List<ExtractionSafeDefinition> SafeDefinitions = new();
        public List<ExtractionHostileExplorerDefinition> HostileExplorerEncounters = new();
        public List<ExtractionBuybackQuoteDefinition> BuybackQuoteDefinitions = new();
        public List<ExtractionFacilityDefinition> FacilityDefinitions = new();
        public List<ExtractionIntelHintDefinition> IntelHintDefinitions = new();
        public List<ExtractionMerchantOfferDefinition> MerchantOfferDefinitions = new();
        public List<ExtractionCraftingRecipeDefinition> CraftingRecipeDefinitions = new();
        public List<ExtractionTrainingPassiveDefinition> TrainingPassiveDefinitions = new();
        public List<ExtractionMedicalTreatmentDefinition> MedicalTreatmentDefinitions = new();
        public List<ExtractionBlueprintDefinition> BlueprintDefinitions = new();
        public List<ExtractionMetaExchangeDefinition> MetaExchangeDefinitions = new();

        // C2 内容配置：旧配置不声明任何一项时继续走既有地图 lootTableIds 路径；
        // 只要开始声明，就由 ExtractionLootContentConfigValidator 校验整套引用。
        public List<ExtractionContentTierDefinition> ContentTiers = new();
        public List<ExtractionLootProfileDefinition> LootProfiles = new();
        public List<ExtractionLootRegionDefinition> LootRegions = new();
        public List<ExtractionContainerDefinition> ContainerDefinitions = new();
        public List<ExtractionContainerSpawnDefinition> ContainerSpawns = new();

        // M2 SW.2：负重容量(总重达到此值=100%负重)+档位表；容量默认 0(=不参与负重系统，
        // TrySelectTier 对 capacity<=0 直接返回 false)，向后兼容不配负重的旧配置/mod。
        public int EncumbranceCapacity;
        public List<ExtractionEncumbranceTierDefinition> EncumbranceTiers = new();

        // M2 SD2.1：难度档位数值。Default/Normal 两档语义是现状行为，SD2.2 直接按枚举分支硬编码
        // 判定，不需要额外可调数值；这里只保存"非默认档"真正需要读取的数值。默认值与 D-M2-6
        // 决策一致(宽松丢一半、敌伤×0.5/×1.5)，向后兼容不配这些字段的旧配置/mod。
        public float DifficultyCorpseLossLenientFraction = 0.5f;
        public float DifficultyEnemyDamageHalfMultiplier = 0.5f;
        public float DifficultyEnemyDamageHardMultiplier = 1.5f;

        public ExtractionPlayableConfig(
            int raidBackpackWidth,
            int raidBackpackHeight,
            int secureContainerWidth,
            int secureContainerHeight)
        {
            RaidBackpackWidth = raidBackpackWidth;
            RaidBackpackHeight = raidBackpackHeight;
            SecureContainerWidth = secureContainerWidth;
            SecureContainerHeight = secureContainerHeight;
        }

        public bool TryGetMap(string mapId, out ExtractionMapDefinition map)
        {
            foreach (var candidate in Maps)
            {
                if (candidate != null && candidate.MapId == mapId)
                {
                    map = candidate;
                    return true;
                }
            }

            map = null;
            return false;
        }

        public bool TryGetExtractionPoint(string pointId, out ExtractionPointDefinition point)
        {
            return TryGetExtractionPoint(pointId, string.Empty, out point);
        }

        public bool TryGetExtractionPoint(
            string pointId,
            string mapId,
            out ExtractionPointDefinition point)
        {
            foreach (var candidate in ExtractionPoints)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.PointId != pointId) continue;
                if (candidate.MapId == mapId && !string.IsNullOrEmpty(candidate.MapId))
                {
                    point = candidate;
                    return true;
                }
            }

            foreach (var candidate in ExtractionPoints)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.PointId != pointId) continue;
                if (!string.IsNullOrEmpty(candidate.MapId)) continue;

                point = candidate;
                return true;
            }

            point = null;
            return false;
        }

        public bool TryGetMapExtractionPoints(string mapId, List<ExtractionPointDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!TryGetMap(mapId, out var map) || map == null || !map.IsValid) return false;

            foreach (var candidate in ExtractionPoints)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.MapId != mapId || string.IsNullOrEmpty(candidate.MapId)) continue;
                AddUniqueExtractionPoint(candidate, results);
            }

            foreach (var candidate in ExtractionPoints)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (!string.IsNullOrEmpty(candidate.MapId)) continue;
                AddUniqueExtractionPoint(candidate, results);
            }

            return results.Count > 0;
        }

        public bool TryGetItemDefinition(string definitionId, out ExtractionItemDefinition definition)
        {
            foreach (var candidate in ItemDefinitions)
            {
                if (candidate != null && candidate.DefinitionId == definitionId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool TryGetBlueprintDefinition(string blueprintId, out ExtractionBlueprintDefinition blueprint)
        {
            foreach (var candidate in BlueprintDefinitions)
            {
                if (candidate != null && candidate.BlueprintId == blueprintId)
                {
                    blueprint = candidate;
                    return true;
                }
            }

            blueprint = null;
            return false;
        }

        public bool TryGetLootTable(string tableId, out ExtractionLootTableDefinition table)
        {
            table = null;
            if (string.IsNullOrEmpty(tableId)) return false;

            foreach (var candidate in LootTables)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.TableId != tableId) continue;

                table = candidate;
                return true;
            }

            return false;
        }

        public bool TryGetMapLootTables(string mapId, List<ExtractionLootTableDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!TryGetMap(mapId, out var map) || map == null || !map.IsValid) return false;
            if (map.LootTableIds == null) return false;

            foreach (var tableId in map.LootTableIds)
            {
                if (!TryGetLootTable(tableId, out var table)) continue;
                if (ContainsLootTable(results, table.TableId)) continue;
                results.Add(table);
            }

            return results.Count > 0;
        }

        private static void AddUniqueExtractionPoint(
            ExtractionPointDefinition point,
            List<ExtractionPointDefinition> results)
        {
            foreach (var result in results)
            {
                if (result != null && result.PointId == point.PointId) return;
            }

            results.Add(point);
        }

        private static bool ContainsLootTable(
            List<ExtractionLootTableDefinition> results,
            string tableId)
        {
            foreach (var table in results)
            {
                if (table != null && table.TableId == tableId) return true;
            }

            return false;
        }

        public bool TryGetMapSpawnPointIds(string mapId, List<string> results)
        {
            if (!TryGetValidMapIdList(mapId, results, out var map)) return false;
            AddUniqueIds(map.SpawnPointIds, results);
            return results.Count > 0;
        }

        public bool TryGetMapCorpseRegionIds(string mapId, List<string> results)
        {
            if (!TryGetValidMapIdList(mapId, results, out var map)) return false;
            AddUniqueIds(map.CorpseRegionIds, results);
            return results.Count > 0;
        }

        public bool TrySelectMapCorpseRegionId(string mapId, int seed, out string regionId)
        {
            regionId = null;
            var results = new List<string>();
            if (!TryGetMapCorpseRegionIds(mapId, results)) return false;

            int index = seed % results.Count;
            if (index < 0) index += results.Count;
            regionId = results[index];
            return true;
        }

        private bool TryGetValidMapIdList(
            string mapId,
            List<string> results,
            out ExtractionMapDefinition map)
        {
            map = null;
            if (results == null) return false;
            results.Clear();
            return TryGetMap(mapId, out map) && map != null && map.IsValid;
        }

        private static void AddUniqueIds(List<string> source, List<string> results)
        {
            if (source == null) return;

            foreach (var id in source)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (results.Contains(id)) continue;
                results.Add(id);
            }
        }

        public bool TryGetFacilityDefinitions(
            ExtractionFacilityType facilityType,
            List<ExtractionFacilityDefinition> results)
        {
            if (results == null) return false;
            results.Clear();

            foreach (var candidate in FacilityDefinitions)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.FacilityType != facilityType) continue;
                results.Add(candidate);
            }

            return results.Count > 0;
        }

        public bool TryGetNextFacilityUpgradePlan(
            ExtractionProfileSaveData profile,
            ExtractionFacilityType facilityType,
            IExtractionMetaProgressProvider metaProgress,
            out ExtractionFacilityUpgradePlan plan)
        {
            return ExtractionFacilityUpgradePlanService.TryGetNextUpgradePlan(
                profile,
                FacilityDefinitions,
                facilityType,
                metaProgress,
                out plan);
        }

        public bool TryGetAvailableIntelHints(
            ExtractionProfileSaveData profile,
            string mapId,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionIntelHintDefinition> results)
        {
            return ExtractionIntelHintService.TryGetAvailableHints(
                profile,
                IntelHintDefinitions,
                mapId,
                metaProgress,
                results);
        }

        public bool TryGetAvailableMerchantOffers(
            ExtractionProfileSaveData profile,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionMerchantOfferDefinition> results)
        {
            return ExtractionMerchantOfferService.TryGetAvailableOffers(
                profile,
                MerchantOfferDefinitions,
                this,
                metaProgress,
                results);
        }

        public bool TryGetAvailableCraftingRecipes(
            ExtractionProfileSaveData profile,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionCraftingRecipeDefinition> results)
        {
            return ExtractionCraftingRecipeService.TryGetAvailableRecipes(
                profile,
                CraftingRecipeDefinitions,
                this,
                metaProgress,
                results);
        }

        public bool TryGetAvailableTrainingPassives(
            ExtractionProfileSaveData profile,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionTrainingPassiveDefinition> results)
        {
            return ExtractionTrainingPassiveService.TryGetAvailablePassives(
                profile,
                TrainingPassiveDefinitions,
                metaProgress,
                results);
        }

        public bool TryGetAvailableMedicalTreatments(
            ExtractionProfileSaveData profile,
            string conditionId,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionMedicalTreatmentDefinition> results)
        {
            return ExtractionMedicalTreatmentService.TryGetAvailableTreatments(
                profile,
                MedicalTreatmentDefinitions,
                this,
                conditionId,
                metaProgress,
                results);
        }

        public bool TryGetSafeDefinitions(string mapId, List<ExtractionSafeDefinition> results)
        {
            return ExtractionSafeLootService.TryGetSafeDefinitions(SafeDefinitions, mapId, results);
        }

        public bool TryGetBuybackQuote(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            out ExtractionBuybackQuote quote)
        {
            return ExtractionBuybackQuoteService.TryGetQuote(
                profile,
                BuybackQuoteDefinitions,
                itemInstanceId,
                out quote);
        }

        public bool TryGetHostileExplorerCandidates(
            string mapId,
            int threatLevel,
            List<ExtractionHostileExplorerDefinition> results)
        {
            return ExtractionHostileExplorerSelectionService.TryGetCandidates(
                HostileExplorerEncounters,
                mapId,
                threatLevel,
                results);
        }

        public bool TrySelectHostileExplorerEncounter(
            string mapId,
            int threatLevel,
            int seed,
            out ExtractionHostileExplorerDefinition encounter)
        {
            encounter = null;
            var candidates = new List<ExtractionHostileExplorerDefinition>();
            if (!TryGetHostileExplorerCandidates(mapId, threatLevel, candidates)) return false;
            return ExtractionHostileExplorerSelectionService.TrySelectCandidateBySeed(
                candidates,
                seed,
                out encounter);
        }
    }
}
