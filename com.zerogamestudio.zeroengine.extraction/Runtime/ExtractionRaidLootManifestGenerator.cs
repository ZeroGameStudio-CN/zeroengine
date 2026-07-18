using System;
using System.Collections.Generic;
using System.Globalization;

namespace POB.Extraction
{
    public static class ExtractionRaidLootManifestGenerator
    {
        private const string ManifestHashDomain = "zeroengine.extraction.loot-manifest:v1";
        private const string SpawnHashDomain = "zeroengine.extraction.container-spawn:v1";
        private const string CandidateHashDomain = "zeroengine.extraction.container-candidate:v1";
        private const string CountHashDomain = "zeroengine.extraction.container-count:v1";
        private const string GuaranteeOrderHashDomain = "zeroengine.extraction.guarantee-order:v1";
        private const string GuaranteeItemHashDomain = "zeroengine.extraction.guarantee-item:v1";
        private const string EntryHashDomain = "zeroengine.extraction.loot-entry:v1";
        private const string InstanceHashDomain = "zeroengine.extraction.loot-instance:v1";

        public static bool TryGenerate(
            ExtractionPlayableConfig config,
            ExtractionMapDefinition map,
            int raidSeed,
            bool rareLootDisabled,
            out ExtractionRaidLootManifest manifest,
            out ExtractionRaidLootManifestFailure failure)
        {
            manifest = null;
            failure = ExtractionRaidLootManifestFailure.None;
            if (config == null || map == null || !map.IsValid)
                return Fail(ExtractionRaidLootManifestFailure.InvalidInput, out failure);

            if (string.IsNullOrEmpty(map.ContentTierId) && string.IsNullOrEmpty(map.LootProfileId))
                return true;

            if (!TryResolveContent(config, map, out var tier, out var profile))
                return Fail(ExtractionRaidLootManifestFailure.MissingContentConfiguration, out failure);

            var result = new ExtractionRaidLootManifest
            {
                ManifestId = ExtractionStableHash.ComputeSha256(
                    ManifestHashDomain,
                    map.MapId,
                    raidSeed.ToString(CultureInfo.InvariantCulture),
                    profile.LootProfileId,
                    rareLootDisabled ? "1" : "0"),
                LootProfileId = profile.LootProfileId,
                ContentTierId = tier.ContentTierId,
                RaidSeed = raidSeed,
                RareLootDisabled = rareLootDisabled
            };

            var spawns = ResolveProfileSpawns(config, profile);
            foreach (var spawn in spawns)
            {
                if (!ShouldSpawn(spawn, raidSeed)) continue;
                if (!TrySelectContainer(config, spawn, raidSeed, out var definition))
                    return Fail(ExtractionRaidLootManifestFailure.MissingContentConfiguration, out failure);

                int maximum = Math.Min(definition.Capacity, definition.MaximumContentCount);
                int target = RollInclusive(
                    definition.MinimumContentCount,
                    maximum,
                    CountHashDomain,
                    raidSeed,
                    spawn.SpawnId);
                var manifestContainer = new ExtractionRaidContainerManifest(
                    spawn.SpawnId,
                    spawn.RegionId,
                    definition.ContainerTypeId,
                    definition.Capacity,
                    target,
                    maximum,
                    definition.SearchTimeMultiplier)
                {
                    BonusGroupId = spawn.BonusGroupId,
                    Active = string.IsNullOrEmpty(spawn.BonusGroupId)
                };
                result.Containers.Add(manifestContainer);
            }

            if (result.Containers.Count == 0)
                return Fail(ExtractionRaidLootManifestFailure.NoSpawnedContainers, out failure);

            if (!TryAssignGuarantees(config, tier, profile, result, out failure))
                return false;

            manifest = result;
            return true;
        }

        private static bool TryAssignGuarantees(
            ExtractionPlayableConfig config,
            ExtractionContentTierDefinition tier,
            ExtractionLootProfileDefinition profile,
            ExtractionRaidLootManifest manifest,
            out ExtractionRaidLootManifestFailure failure)
        {
            failure = ExtractionRaidLootManifestFailure.None;
            for (int rarityValue = (int)ExtractionItemRarity.Mythic;
                 rarityValue >= (int)ExtractionItemRarity.Common;
                 rarityValue--)
            {
                var rarity = (ExtractionItemRarity)rarityValue;
                int required = ExtractionLootContentPolicy.GetMinimumGeneratedDrops(
                    profile,
                    rarity,
                    manifest.RareLootDisabled);
                for (int index = 0; index < required; index++)
                {
                    var candidates = GetGuaranteeContainerCandidates(config, manifest, rarity, index);
                    if (candidates.Count == 0)
                    {
                        return Fail(
                            HasAnyCandidateForRarity(config, manifest, rarity)
                                ? ExtractionRaidLootManifestFailure.InsufficientGuaranteedCapacity
                                : ExtractionRaidLootManifestFailure.MissingRarityCandidate,
                            out failure);
                    }

                    var container = candidates[0];
                    if (!TrySelectLootEntry(
                            config,
                            tier,
                            container.RegionId,
                            container.ContainerTypeId,
                            rarity,
                            manifest.RaidSeed,
                            GuaranteeItemHashDomain,
                            container.ContainerId,
                            index.ToString(CultureInfo.InvariantCulture),
                            out var tableEntry,
                            out var itemDefinition,
                            rareLootDisabled: manifest.RareLootDisabled))
                    {
                        return Fail(ExtractionRaidLootManifestFailure.MissingRarityCandidate, out failure);
                    }

                    string entryId = CreateStableId(
                        "entry:v1:",
                        EntryHashDomain,
                        manifest.ManifestId,
                        container.ContainerId,
                        ((int)rarity).ToString(CultureInfo.InvariantCulture),
                        index.ToString(CultureInfo.InvariantCulture));
                    string instanceId = CreateStableId(
                        "item:v1:",
                        InstanceHashDomain,
                        manifest.ManifestId,
                        entryId);
                    container.Entries.Add(new ExtractionContainerLootEntry(
                        entryId,
                        instanceId,
                        tableEntry.DefinitionId,
                        tableEntry.Quantity,
                        itemDefinition.Rarity,
                        true));
                    if (container.TargetContentCount < container.Entries.Count)
                        container.TargetContentCount = container.Entries.Count;
                }
            }

            return true;
        }

        private static List<ExtractionRaidContainerManifest> GetGuaranteeContainerCandidates(
            ExtractionPlayableConfig config,
            ExtractionRaidLootManifest manifest,
            ExtractionItemRarity rarity,
            int guaranteeIndex)
        {
            var results = new List<ExtractionRaidContainerManifest>();
            foreach (var container in manifest.Containers)
            {
                if (container == null || !container.Active || container.Entries.Count >= container.MaximumContentCount) continue;
                if (!ContainerHasRarityCandidate(config, container.ContainerTypeId, rarity, manifest.RareLootDisabled))
                    continue;
                results.Add(container);
            }

            results.Sort((left, right) =>
            {
                int leftHash = ExtractionStableHash.ComputeInt32(
                    GuaranteeOrderHashDomain,
                    manifest.RaidSeed.ToString(CultureInfo.InvariantCulture),
                    ((int)rarity).ToString(CultureInfo.InvariantCulture),
                    guaranteeIndex.ToString(CultureInfo.InvariantCulture),
                    left.ContainerId);
                int rightHash = ExtractionStableHash.ComputeInt32(
                    GuaranteeOrderHashDomain,
                    manifest.RaidSeed.ToString(CultureInfo.InvariantCulture),
                    ((int)rarity).ToString(CultureInfo.InvariantCulture),
                    guaranteeIndex.ToString(CultureInfo.InvariantCulture),
                    right.ContainerId);
                int comparison = unchecked((uint)leftHash).CompareTo(unchecked((uint)rightHash));
                return comparison != 0
                    ? comparison
                    : string.CompareOrdinal(left.ContainerId, right.ContainerId);
            });
            return results;
        }

        private static bool HasAnyCandidateForRarity(
            ExtractionPlayableConfig config,
            ExtractionRaidLootManifest manifest,
            ExtractionItemRarity rarity)
        {
            foreach (var container in manifest.Containers)
            {
                if (container != null
                    && ContainerHasRarityCandidate(
                        config,
                        container.ContainerTypeId,
                        rarity,
                        manifest.RareLootDisabled))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool TrySelectLootEntry(
            ExtractionPlayableConfig config,
            ExtractionContentTierDefinition tier,
            string regionId,
            string containerTypeId,
            ExtractionItemRarity? exactRarity,
            int raidSeed,
            string hashDomain,
            string identityA,
            string identityB,
            out ExtractionLootTableEntry selectedEntry,
            out ExtractionItemDefinition selectedDefinition,
            int pityMisses = 0,
            ExtractionLootPityDefinition pity = null,
            bool rareLootDisabled = false)
        {
            selectedEntry = null;
            selectedDefinition = null;
            if (!TryGetContainer(config, containerTypeId, out var container)
                || !TryGetRegion(config, regionId, out var region))
            {
                return false;
            }

            var candidates = new List<WeightedLootCandidate>();
            double totalWeight = 0d;
            foreach (string tableId in container.LootTableIds)
            {
                if (!config.TryGetLootTable(tableId, out var table)
                    || !ExtractionLootContentPolicy.IsLootTableEnabled(table, rareLootDisabled))
                {
                    continue;
                }

                foreach (var entry in table.Entries)
                {
                    if (entry == null || !entry.IsValid) continue;
                    if (!config.TryGetItemDefinition(entry.DefinitionId, out var definition) || definition == null)
                        continue;
                    if (exactRarity.HasValue && definition.Rarity != exactRarity.Value) continue;
                    if (!ExtractionLootContentPolicy.IsRarityEnabled(definition.Rarity, rareLootDisabled)) continue;

                    double weight = entry.Weight
                                    * Math.Max(0d, tier?.RarityWeightMultipliers?.Get(definition.Rarity) ?? 1d)
                                    * Math.Max(0d, region.RarityWeightMultipliers?.Get(definition.Rarity) ?? 1d);
                    if (pity != null && definition.Rarity >= pity.TargetRarity)
                    {
                        double pityMultiplier = Math.Min(
                            pity.MaximumWeightMultiplier,
                            1d + pity.WeightMultiplierIncrementPerMiss * Math.Max(0, pityMisses));
                        weight *= Math.Max(1d, pityMultiplier);
                    }

                    if (weight <= 0d) continue;
                    totalWeight += weight;
                    candidates.Add(new WeightedLootCandidate(entry, definition, totalWeight));
                }
            }

            if (candidates.Count == 0 || totalWeight <= 0d) return false;
            double target = StableUnit(
                hashDomain,
                raidSeed.ToString(CultureInfo.InvariantCulture),
                identityA,
                identityB) * totalWeight;
            foreach (var candidate in candidates)
            {
                if (target >= candidate.CumulativeWeight) continue;
                selectedEntry = candidate.Entry;
                selectedDefinition = candidate.Definition;
                return true;
            }

            var fallback = candidates[candidates.Count - 1];
            selectedEntry = fallback.Entry;
            selectedDefinition = fallback.Definition;
            return true;
        }

        private static bool ContainerHasRarityCandidate(
            ExtractionPlayableConfig config,
            string containerTypeId,
            ExtractionItemRarity rarity,
            bool rareLootDisabled)
        {
            if (!TryGetContainer(config, containerTypeId, out var container)) return false;
            foreach (string tableId in container.LootTableIds)
            {
                if (!config.TryGetLootTable(tableId, out var table)
                    || !ExtractionLootContentPolicy.IsLootTableEnabled(table, rareLootDisabled))
                {
                    continue;
                }

                foreach (var entry in table.Entries)
                {
                    if (entry != null
                        && entry.IsValid
                        && config.TryGetItemDefinition(entry.DefinitionId, out var definition)
                        && definition != null
                        && definition.Rarity == rarity
                        && ExtractionLootContentPolicy.IsRarityEnabled(rarity, rareLootDisabled))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<ExtractionContainerSpawnDefinition> ResolveProfileSpawns(
            ExtractionPlayableConfig config,
            ExtractionLootProfileDefinition profile)
        {
            var regionIds = new HashSet<string>(profile.RegionIds ?? new List<string>());
            var results = new List<ExtractionContainerSpawnDefinition>();
            foreach (var spawn in config.ContainerSpawns)
            {
                if (spawn != null && regionIds.Contains(spawn.RegionId))
                    results.Add(spawn);
            }

            results.Sort((left, right) => string.CompareOrdinal(left.SpawnId, right.SpawnId));
            return results;
        }

        private static bool ShouldSpawn(ExtractionContainerSpawnDefinition spawn, int raidSeed)
        {
            if (spawn.Always) return true;
            if (!spawn.ChancePerRaid) return false;
            return StableUnit(
                       SpawnHashDomain,
                       raidSeed.ToString(CultureInfo.InvariantCulture),
                       spawn.SpawnId)
                   < spawn.Chance;
        }

        private static bool TrySelectContainer(
            ExtractionPlayableConfig config,
            ExtractionContainerSpawnDefinition spawn,
            int raidSeed,
            out ExtractionContainerDefinition definition)
        {
            definition = null;
            long totalWeight = 0;
            foreach (var candidate in spawn.Candidates)
                if (candidate != null && candidate.Weight > 0) totalWeight += candidate.Weight;
            if (totalWeight <= 0) return false;

            long target = unchecked((uint)ExtractionStableHash.ComputeInt32(
                              CandidateHashDomain,
                              raidSeed.ToString(CultureInfo.InvariantCulture),
                              spawn.SpawnId)) % totalWeight;
            long cursor = 0;
            foreach (var candidate in spawn.Candidates)
            {
                if (candidate == null || candidate.Weight <= 0) continue;
                cursor += candidate.Weight;
                if (target >= cursor) continue;
                return TryGetContainer(config, candidate.ContainerTypeId, out definition);
            }

            return false;
        }

        private static int RollInclusive(int minimum, int maximum, string domain, int seed, string identity)
        {
            if (maximum <= minimum) return minimum;
            uint value = unchecked((uint)ExtractionStableHash.ComputeInt32(
                domain,
                seed.ToString(CultureInfo.InvariantCulture),
                identity));
            return minimum + (int)(value % (uint)(maximum - minimum + 1));
        }

        private static double StableUnit(string domain, params string[] values)
        {
            uint value = unchecked((uint)ExtractionStableHash.ComputeInt32(domain, values));
            return value / ((double)uint.MaxValue + 1d);
        }

        private static bool TryResolveContent(
            ExtractionPlayableConfig config,
            ExtractionMapDefinition map,
            out ExtractionContentTierDefinition tier,
            out ExtractionLootProfileDefinition profile)
        {
            tier = null;
            profile = null;
            foreach (var candidate in config.ContentTiers)
                if (candidate != null && candidate.ContentTierId == map.ContentTierId) tier = candidate;
            foreach (var candidate in config.LootProfiles)
                if (candidate != null && candidate.LootProfileId == map.LootProfileId) profile = candidate;
            return tier != null && profile != null && profile.ContentTierId == tier.ContentTierId;
        }

        internal static bool TryGetContentDefinitions(
            ExtractionPlayableConfig config,
            ExtractionRaidLootManifest manifest,
            out ExtractionContentTierDefinition tier,
            out ExtractionLootProfileDefinition profile)
        {
            tier = null;
            profile = null;
            if (config == null || manifest == null) return false;
            foreach (var candidate in config.ContentTiers)
                if (candidate != null && candidate.ContentTierId == manifest.ContentTierId) tier = candidate;
            foreach (var candidate in config.LootProfiles)
                if (candidate != null && candidate.LootProfileId == manifest.LootProfileId) profile = candidate;
            return tier != null && profile != null;
        }

        internal static bool TryGetContainer(
            ExtractionPlayableConfig config,
            string containerTypeId,
            out ExtractionContainerDefinition definition)
        {
            foreach (var candidate in config.ContainerDefinitions)
            {
                if (candidate != null && candidate.ContainerTypeId == containerTypeId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        internal static bool TryGetRegion(
            ExtractionPlayableConfig config,
            string regionId,
            out ExtractionLootRegionDefinition definition)
        {
            foreach (var candidate in config.LootRegions)
            {
                if (candidate != null && candidate.RegionId == regionId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        internal static string CreateStableId(string prefix, string domain, params string[] values)
        {
            string hash = ExtractionStableHash.ComputeSha256(domain, values);
            return prefix + hash.Substring("sha256:".Length);
        }

        private static bool Fail(
            ExtractionRaidLootManifestFailure value,
            out ExtractionRaidLootManifestFailure failure)
        {
            failure = value;
            return false;
        }

        private sealed class WeightedLootCandidate
        {
            public readonly ExtractionLootTableEntry Entry;
            public readonly ExtractionItemDefinition Definition;
            public readonly double CumulativeWeight;

            public WeightedLootCandidate(
                ExtractionLootTableEntry entry,
                ExtractionItemDefinition definition,
                double cumulativeWeight)
            {
                Entry = entry;
                Definition = definition;
                CumulativeWeight = cumulativeWeight;
            }
        }
    }
}
