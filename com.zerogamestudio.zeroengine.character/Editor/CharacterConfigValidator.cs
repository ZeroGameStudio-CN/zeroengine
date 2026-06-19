using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Character.Job;
using ZeroEngine.Character.MartialArts;
using ZeroEngine.Character.Realm;
using ZeroEngine.Character.Sect;
using ZeroEngine.Equipment;
using ZeroEngine.Party;
using ZeroEngine.TalentTree;

namespace ZeroEngine.Character.Editor
{
    public enum CharacterValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class CharacterValidationIssue
    {
        public CharacterValidationIssue(
            CharacterValidationSeverity severity,
            string assetName,
            string fieldPath,
            string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public CharacterValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class CharacterConfigValidator
    {
        public static IReadOnlyList<CharacterValidationIssue> Validate(
            IEnumerable<EquipmentDataSO> equipment = null,
            IEnumerable<EquipmentSetSO> equipmentSets = null,
            IEnumerable<EquipmentSlotType> equipmentSlots = null,
            IEnumerable<JobDataSO> jobs = null,
            IEnumerable<JobSkillSO> jobSkills = null,
            IEnumerable<JobPassiveSO> jobPassives = null,
            IEnumerable<JobDatabaseSO> jobDatabases = null,
            IEnumerable<MartialArtDataSO> martialArts = null,
            IEnumerable<MartialArtDatabaseSO> martialArtDatabases = null,
            IEnumerable<RealmDataSO> realms = null,
            IEnumerable<RealmDatabaseSO> realmDatabases = null,
            IEnumerable<SectDataSO> sects = null,
            IEnumerable<SectDatabaseSO> sectDatabases = null,
            IEnumerable<PartyConfigSO> partyConfigs = null,
            IEnumerable<FormationSO> formations = null,
            IEnumerable<TalentNodeSO> talentNodes = null,
            IEnumerable<TalentTreeSO> talentTrees = null)
        {
            bool loadAll = equipment == null
                           && equipmentSets == null
                           && equipmentSlots == null
                           && jobs == null
                           && jobSkills == null
                           && jobPassives == null
                           && jobDatabases == null
                           && martialArts == null
                           && martialArtDatabases == null
                           && realms == null
                           && realmDatabases == null
                           && sects == null
                           && sectDatabases == null
                           && partyConfigs == null
                           && formations == null
                           && talentNodes == null
                           && talentTrees == null;

            var equipmentList = Resolve(equipment, loadAll);
            var equipmentSetList = Resolve(equipmentSets, loadAll);
            var equipmentSlotList = Resolve(equipmentSlots, loadAll);
            var jobList = Resolve(jobs, loadAll);
            var jobSkillList = Resolve(jobSkills, loadAll);
            var jobPassiveList = Resolve(jobPassives, loadAll);
            var jobDatabaseList = Resolve(jobDatabases, loadAll);
            var martialArtList = Resolve(martialArts, loadAll);
            var martialArtDatabaseList = Resolve(martialArtDatabases, loadAll);
            var realmList = Resolve(realms, loadAll);
            var realmDatabaseList = Resolve(realmDatabases, loadAll);
            var sectList = Resolve(sects, loadAll);
            var sectDatabaseList = Resolve(sectDatabases, loadAll);
            var partyConfigList = Resolve(partyConfigs, loadAll);
            var formationList = Resolve(formations, loadAll);
            var talentNodeList = Resolve(talentNodes, loadAll);
            var talentTreeList = Resolve(talentTrees, loadAll);

            var issues = new List<CharacterValidationIssue>();

            AddDuplicateStringIssues(issues, equipmentList, x => x.Id, "Id", "Equipment Id");
            AddDuplicateStringIssues(issues, equipmentSetList, x => x.SetId, "SetId", "Equipment set ID");
            AddDuplicateStringIssues(issues, equipmentSlotList, x => x.SlotId, "SlotId", "Equipment slot ID");
            AddDuplicateEnumIssues(issues, jobList, x => x.JobType, JobType.None, "JobType", "Job type");
            AddDuplicateStringIssues(issues, jobSkillList, x => x.SkillId, "SkillId", "Job skill ID");
            AddDuplicateStringIssues(issues, jobPassiveList, x => x.PassiveId, "PassiveId", "Job passive ID");
            AddDuplicateStringIssues(issues, martialArtList, x => x.artId, "artId", "Martial art ID");
            AddDuplicateEnumIssues(issues, realmList, x => x.realmType, RealmType.None, "realmType", "Realm type");
            AddDuplicateEnumIssues(issues, sectList, x => x.sectType, SectType.None, "sectType", "Sect type");
            AddDuplicateStringIssues(issues, talentNodeList, x => x.NodeId, "NodeId", "Talent node ID");
            AddDuplicateStringIssues(issues, talentTreeList, x => x.TreeId, "TreeId", "Talent tree ID");

            foreach (var item in equipmentList)
                ValidateEquipment(issues, item);
            foreach (var set in equipmentSetList)
                ValidateEquipmentSet(issues, set);
            foreach (var slot in equipmentSlotList)
                ValidateEquipmentSlot(issues, slot);
            foreach (var job in jobList)
                ValidateJob(issues, job);
            foreach (var skill in jobSkillList)
                ValidateJobSkill(issues, skill);
            foreach (var passive in jobPassiveList)
                ValidateJobPassive(issues, passive);
            foreach (var database in jobDatabaseList)
                ValidateJobDatabase(issues, database);
            foreach (var art in martialArtList)
                ValidateMartialArt(issues, art);
            foreach (var database in martialArtDatabaseList)
                ValidateMartialArtDatabase(issues, database);
            foreach (var realm in realmList)
                ValidateRealm(issues, realm);
            foreach (var database in realmDatabaseList)
                ValidateRealmDatabase(issues, database);
            foreach (var sect in sectList)
                ValidateSect(issues, sect);
            foreach (var database in sectDatabaseList)
                ValidateSectDatabase(issues, database);
            foreach (var config in partyConfigList)
                ValidatePartyConfig(issues, config);
            foreach (var formation in formationList)
                ValidateFormation(issues, formation);
            foreach (var node in talentNodeList)
                ValidateTalentNode(issues, node);
            foreach (var tree in talentTreeList)
                ValidateTalentTree(issues, tree);

            return issues;
        }

        public static IReadOnlyList<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            string typeFilter = $"t:{typeof(T).Name}";
            return AssetDatabase.FindAssets(typeFilter)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }

        private static List<T> Resolve<T>(IEnumerable<T> source, bool loadAll) where T : UnityEngine.Object
        {
            if (source != null)
                return source.ToList();

            return loadAll ? LoadAssets<T>().ToList() : new List<T>();
        }

        private static void ValidateEquipment(List<CharacterValidationIssue> issues, EquipmentDataSO item)
        {
            if (item == null)
                return;

            RequireText(issues, item, "Id", item.Id, "Equipment must have a stable item ID.");
            RequireText(issues, item, "ItemName", item.ItemName, "Equipment must have a designer-facing item name.");
            RequireText(issues, item, "Description", item.Description, "Equipment should explain its use and fantasy.");

            if (item.SlotType == null)
                Add(issues, CharacterValidationSeverity.Error, item, "SlotType", "Equipment must reference an equipment slot type.");
            if (item.MaxEnhanceLevel < 0)
                Add(issues, CharacterValidationSeverity.Error, item, "MaxEnhanceLevel", "Max enhance level cannot be negative.");
            if (item.MaxRefineLevel < 0)
                Add(issues, CharacterValidationSeverity.Error, item, "MaxRefineLevel", "Max refine level cannot be negative.");
            if (item.GemSlotCount < 0)
                Add(issues, CharacterValidationSeverity.Error, item, "GemSlotCount", "Gem slot count cannot be negative.");
            if (item.GemSlotCount > 0 && item.GemSlotsPerRefine <= 0)
                Add(issues, CharacterValidationSeverity.Error, item, "GemSlotsPerRefine", "Gem slots per refine must be positive when gem slots exist.");
            if (!IsNormalizedRate(item.BaseEnhanceSuccessRate))
                Add(issues, CharacterValidationSeverity.Error, item, "BaseEnhanceSuccessRate", "Enhance success rate must be between 0 and 1.");
            if (item.SuccessRateDecayPerLevel < 0f || item.SuccessRateDecayPerLevel > 1f)
                Add(issues, CharacterValidationSeverity.Error, item, "SuccessRateDecayPerLevel", "Success rate decay must be between 0 and 1.");
            if (item.RequiredLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, item, "RequiredLevel", "Equipment required level must be positive.");

            ValidateStatModifierDataList(issues, item, "BaseStats", item.BaseStats);
        }

        private static void ValidateEquipmentSet(List<CharacterValidationIssue> issues, EquipmentSetSO set)
        {
            if (set == null)
                return;

            RequireText(issues, set, "SetId", set.SetId, "Equipment set must have a stable ID.");
            RequireText(issues, set, "SetName", set.SetName, "Equipment set must have a display name.");
            RequireText(issues, set, "Description", set.Description, "Equipment set should describe its theme.");

            var pieces = set.Pieces ?? new List<EquipmentDataSO>();
            var pieceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, set, $"Pieces[{i}]", "Equipment set contains an empty piece reference.");
                    continue;
                }

                string id = Normalize(piece.Id);
                if (!string.IsNullOrEmpty(id) && !pieceIds.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, set, $"Pieces[{i}]", $"Duplicate equipment piece ID '{id}' in set.");
            }

            var thresholds = new HashSet<int>();
            var effects = set.Effects ?? new List<SetEffect>();
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, set, $"Effects[{i}]", "Equipment set contains an empty effect.");
                    continue;
                }

                if (effect.RequiredPieces <= 0)
                    Add(issues, CharacterValidationSeverity.Error, set, $"Effects[{i}].RequiredPieces", "Required piece count must be positive.");
                if (pieces.Count > 0 && effect.RequiredPieces > pieces.Count)
                    Add(issues, CharacterValidationSeverity.Error, set, $"Effects[{i}].RequiredPieces", "Required piece count exceeds the number of configured set pieces.");
                if (!thresholds.Add(effect.RequiredPieces))
                    Add(issues, CharacterValidationSeverity.Error, set, $"Effects[{i}].RequiredPieces", "Set effects cannot reuse the same piece threshold.");
                RequireText(issues, set, $"Effects[{i}].Description", effect.Description, "Set effect should have a designer-facing description.", CharacterValidationSeverity.Warning);
                ValidateStatModifierDataList(issues, set, $"Effects[{i}].StatBonuses", effect.StatBonuses);
            }
        }

        private static void ValidateEquipmentSlot(List<CharacterValidationIssue> issues, EquipmentSlotType slot)
        {
            if (slot == null)
                return;

            RequireText(issues, slot, "SlotId", slot.SlotId, "Equipment slot must have a stable ID.");
            RequireText(issues, slot, "DisplayName", slot.DisplayName, "Equipment slot must have a display name.");
            RequireText(issues, slot, "Description", slot.Description, "Equipment slot should describe what can be equipped.");

            if (slot.AllowedTags == null)
                return;

            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < slot.AllowedTags.Length; i++)
            {
                string tag = Normalize(slot.AllowedTags[i]);
                if (string.IsNullOrEmpty(tag))
                {
                    Add(issues, CharacterValidationSeverity.Warning, slot, $"AllowedTags[{i}]", "Empty equipment tags make filtering ambiguous.");
                    continue;
                }

                if (!tags.Add(tag))
                    Add(issues, CharacterValidationSeverity.Warning, slot, $"AllowedTags[{i}]", $"Duplicate allowed tag '{tag}'.");
            }
        }

        private static void ValidateJob(List<CharacterValidationIssue> issues, JobDataSO job)
        {
            if (job == null)
                return;

            if (job.JobType == JobType.None)
                Add(issues, CharacterValidationSeverity.Error, job, "JobType", "Job type cannot be None.");
            RequireText(issues, job, "DisplayName", job.DisplayName, "Job must have a display name.");
            RequireText(issues, job, "Description", job.Description, "Job must have a description.");
            if (job.MaxJobLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, job, "MaxJobLevel", "Max job level must be positive.");
            if (job.AllowedWeapons == WeaponCategory.None)
                Add(issues, CharacterValidationSeverity.Warning, job, "AllowedWeapons", "Job has no allowed weapon categories.");

            ValidateJobSkillReferences(issues, job, "Skills", job.Skills);
            ValidateJobPassiveReferences(issues, job, "ExclusivePassives", job.ExclusivePassives);

            var unlockConditions = job.UnlockConditions ?? new List<IJobUnlockCondition>();
            if (!job.IsUnlockedByDefault && unlockConditions.All(condition => condition == null))
                Add(issues, CharacterValidationSeverity.Error, job, "UnlockConditions", "Locked jobs must declare at least one unlock condition.");

            ValidateJobUnlockConditions(issues, job, unlockConditions);
        }

        private static void ValidateJobSkill(List<CharacterValidationIssue> issues, JobSkillSO skill)
        {
            if (skill == null)
                return;

            RequireText(issues, skill, "SkillId", skill.SkillId, "Job skill must have a stable ID.");
            RequireText(issues, skill, "DisplayName", skill.DisplayName, "Job skill must have a display name.");
            RequireText(issues, skill, "Description", skill.Description, "Job skill must have a description.");
            if (skill.RequiredJobLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, skill, "RequiredJobLevel", "Required job level must be positive.");
            if (skill.JPCost < 0)
                Add(issues, CharacterValidationSeverity.Error, skill, "JPCost", "JP cost cannot be negative.");
            if (skill.MasterySPCost < 0)
                Add(issues, CharacterValidationSeverity.Error, skill, "MasterySPCost", "Mastery SP cost cannot be negative.");
            if ((skill.Category == JobSkillCategory.Active || skill.Category == JobSkillCategory.Ultimate)
                && string.IsNullOrWhiteSpace(skill.AbilityDataId))
                Add(issues, CharacterValidationSeverity.Warning, skill, "AbilityDataId", "Active or ultimate skills should link to ability data.");

            ValidateStringIds(
                issues,
                skill,
                "PrerequisiteSkillIds",
                skill.PrerequisiteSkillIds,
                id => string.Equals(id, skill.SkillId, StringComparison.OrdinalIgnoreCase)
                    ? "Skill cannot list itself as a prerequisite."
                    : null);
            ValidateSkillStatModifiers(issues, skill, "StatModifiers", skill.StatModifiers);
            ValidateJobSkillConditions(issues, skill, skill.UnlockConditions ?? new List<IJobSkillCondition>());
        }

        private static void ValidateJobPassive(List<CharacterValidationIssue> issues, JobPassiveSO passive)
        {
            if (passive == null)
                return;

            RequireText(issues, passive, "PassiveId", passive.PassiveId, "Job passive must have a stable ID.");
            RequireText(issues, passive, "DisplayName", passive.DisplayName, "Job passive must have a display name.");
            RequireText(issues, passive, "Description", passive.Description, "Job passive must have a description.");
            if (passive.RequiredJobLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, passive, "RequiredJobLevel", "Required job level must be positive.");
            if (string.IsNullOrWhiteSpace(passive.BuffId) && (passive.StatModifiers == null || passive.StatModifiers.Count == 0))
                Add(issues, CharacterValidationSeverity.Warning, passive, "BuffId", "Passive has neither a BuffId nor stat modifiers.");

            ValidateSkillStatModifiers(issues, passive, "StatModifiers", passive.StatModifiers);
        }

        private static void ValidateJobDatabase(List<CharacterValidationIssue> issues, JobDatabaseSO database)
        {
            if (database == null)
                return;

            var allJobs = new List<JobDataSO>();
            ValidateJobDatabaseList(issues, database, "BasicJobs", database.BasicJobs, allJobs);
            ValidateJobDatabaseList(issues, database, "AdvancedJobs", database.AdvancedJobs, allJobs);
            ValidateJobDatabaseList(issues, database, "SecretJobs", database.SecretJobs, allJobs);
            ValidateJobDatabaseList(issues, database, "CustomJobs", database.CustomJobs, allJobs);

            if (allJobs.Count == 0)
                Add(issues, CharacterValidationSeverity.Warning, database, "AllJobs", "Job database does not contain any jobs.");

            AddDuplicateEnumIssues(issues, allJobs, x => x.JobType, JobType.None, "AllJobs", "Job database contains duplicate job types", database);
        }

        private static void ValidateMartialArt(List<CharacterValidationIssue> issues, MartialArtDataSO art)
        {
            if (art == null)
                return;

            RequireText(issues, art, "artId", art.artId, "Martial art must have a stable ID.");
            RequireText(issues, art, "artName", art.artName, "Martial art must have a display name.");
            RequireText(issues, art, "description", art.description, "Martial art must have a description.");
            if (art.artType == MartialArtType.None)
                Add(issues, CharacterValidationSeverity.Error, art, "artType", "Martial art type cannot be None.");
            if (art.maxLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, art, "maxLevel", "Max level must be positive.");
            if (art.expPerLevel == null || art.expPerLevel.Length == 0)
            {
                Add(issues, CharacterValidationSeverity.Error, art, "expPerLevel", "Martial art must define level experience values.");
            }
            else
            {
                if (art.maxLevel > 0 && art.expPerLevel.Length < art.maxLevel)
                    Add(issues, CharacterValidationSeverity.Warning, art, "expPerLevel", "Experience table is shorter than max level and will reuse the last value.");

                for (int i = 0; i < art.expPerLevel.Length; i++)
                {
                    if (art.expPerLevel[i] <= 0)
                        Add(issues, CharacterValidationSeverity.Error, art, $"expPerLevel[{i}]", "Experience requirements must be positive.");
                }
            }

            if (art.cultivationSpeedMultiplier <= 0f)
                Add(issues, CharacterValidationSeverity.Error, art, "cultivationSpeedMultiplier", "Cultivation speed multiplier must be positive.");

            ValidateMartialSkillEntries(issues, art);
            ValidateLearnRequirements(issues, art);

            if (art.hasSideEffect && string.IsNullOrWhiteSpace(art.sideEffectDesc))
                Add(issues, CharacterValidationSeverity.Error, art, "sideEffectDesc", "Forbidden or side-effect martial arts must describe the side effect.");
        }

        private static void ValidateMartialArtDatabase(List<CharacterValidationIssue> issues, MartialArtDatabaseSO database)
        {
            if (database == null)
                return;

            var entries = database.GetAllMartialArts();
            if (entries == null || entries.Count == 0)
            {
                Add(issues, CharacterValidationSeverity.Warning, database, "MartialArts", "Martial art database is empty.");
                return;
            }

            ValidateDatabaseReferences(issues, database, "MartialArts", entries, art => art.artId, "martial art ID");
        }

        private static void ValidateRealm(List<CharacterValidationIssue> issues, RealmDataSO realm)
        {
            if (realm == null)
                return;

            if (realm.realmType == RealmType.None)
                Add(issues, CharacterValidationSeverity.Error, realm, "realmType", "Realm type cannot be None.");
            RequireText(issues, realm, "realmName", realm.realmName, "Realm must have a display name.");
            RequireText(issues, realm, "description", realm.description, "Realm must have a description.");
            if (realm.cultivationRequired < 0)
                Add(issues, CharacterValidationSeverity.Error, realm, "cultivationRequired", "Cultivation requirement cannot be negative.");
            if (realm.cultivationSpeedMultiplier <= 0f)
                Add(issues, CharacterValidationSeverity.Error, realm, "cultivationSpeedMultiplier", "Cultivation speed multiplier must be positive.");
            if (realm.dailyCultivationRegen < 0)
                Add(issues, CharacterValidationSeverity.Warning, realm, "dailyCultivationRegen", "Negative daily cultivation regeneration should be intentional and documented.");
            ValidatePercent(issues, realm, "baseBreakthroughChance", realm.baseBreakthroughChance);
            ValidatePercent(issues, realm, "regressionChance", realm.regressionChance);
            ValidatePercent(issues, realm, "deviationChance", realm.deviationChance);

            if (realm.canRegress && realm.regressionChance <= 0)
                Add(issues, CharacterValidationSeverity.Warning, realm, "regressionChance", "Regression is enabled but chance is zero.");
            if (realm.canDeviate && realm.deviationChance <= 0)
                Add(issues, CharacterValidationSeverity.Warning, realm, "deviationChance", "Deviation is enabled but chance is zero.");

            ValidateBreakthroughRequirements(issues, realm);
            ValidateStringIds(issues, realm, "unlockedAbilities", realm.unlockedAbilities, null, CharacterValidationSeverity.Warning);
            ValidateEnumList(issues, realm, "unlockedMartialArtTypes", realm.unlockedMartialArtTypes, MartialArtType.None);
        }

        private static void ValidateRealmDatabase(List<CharacterValidationIssue> issues, RealmDatabaseSO database)
        {
            if (database == null)
                return;

            var entries = database.GetAllRealms();
            if (entries == null || entries.Count == 0)
            {
                Add(issues, CharacterValidationSeverity.Warning, database, "Realms", "Realm database is empty.");
                return;
            }

            ValidateDatabaseReferences(issues, database, "Realms", entries, realm => realm.realmType.ToString(), "realm type");
        }

        private static void ValidateSect(List<CharacterValidationIssue> issues, SectDataSO sect)
        {
            if (sect == null)
                return;

            if (sect.sectType == SectType.None)
                Add(issues, CharacterValidationSeverity.Error, sect, "sectType", "Sect type cannot be None.");
            RequireText(issues, sect, "sectName", sect.sectName, "Sect must have a display name.");
            RequireText(issues, sect, "description", sect.description, "Sect must have a description.");
            if (sect.requiresUnlock && string.IsNullOrWhiteSpace(sect.unlockConditionDesc))
                Add(issues, CharacterValidationSeverity.Error, sect, "unlockConditionDesc", "Sects that require unlocks must describe the unlock condition.");
            if (sect.minRealmLevel < 0)
                Add(issues, CharacterValidationSeverity.Error, sect, "minRealmLevel", "Minimum realm level cannot be negative.");

            ValidateStringIds(issues, sect, "specialAbilities", sect.specialAbilities, null, CharacterValidationSeverity.Warning);
            ValidateSectMartialArts(issues, sect);
            ValidateRankRequirements(issues, sect);
            ValidateSectRelationships(issues, sect);
        }

        private static void ValidateSectDatabase(List<CharacterValidationIssue> issues, SectDatabaseSO database)
        {
            if (database == null)
                return;

            var entries = database.GetAllSects();
            if (entries == null || entries.Count == 0)
            {
                Add(issues, CharacterValidationSeverity.Warning, database, "Sects", "Sect database is empty.");
                return;
            }

            ValidateDatabaseReferences(issues, database, "Sects", entries, sect => sect.sectType.ToString(), "sect type");
        }

        private static void ValidatePartyConfig(List<CharacterValidationIssue> issues, PartyConfigSO config)
        {
            if (config == null)
                return;

            if (config.MaxActiveMembers <= 0)
                Add(issues, CharacterValidationSeverity.Error, config, "MaxActiveMembers", "Party must allow at least one active member.");
            if (config.MaxReserveMembers < 0)
                Add(issues, CharacterValidationSeverity.Error, config, "MaxReserveMembers", "Reserve member count cannot be negative.");
            if (config.MaxTemporarySlots < 0)
                Add(issues, CharacterValidationSeverity.Error, config, "MaxTemporarySlots", "Temporary slot count cannot be negative.");
            if (config.MaxPetSlots < 0)
                Add(issues, CharacterValidationSeverity.Error, config, "MaxPetSlots", "Pet slot count cannot be negative.");
            if (config.TotalSlots <= 0)
                Add(issues, CharacterValidationSeverity.Error, config, "TotalSlots", "Party total slots must be positive.");
        }

        private static void ValidateFormation(List<CharacterValidationIssue> issues, FormationSO formation)
        {
            if (formation == null)
                return;

            RequireText(issues, formation, "FormationName", formation.FormationName, "Formation must have a display name.");
            RequireText(issues, formation, "Description", formation.Description, "Formation must have a description.");
            if (formation.SpacingScale <= 0f)
                Add(issues, CharacterValidationSeverity.Error, formation, "SpacingScale", "Formation spacing scale must be positive.");
            if (formation.RequiredPartyLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, formation, "RequiredPartyLevel", "Required party level must be positive.");
            if (!formation.UsableInCombat && !formation.UsableInExploration)
                Add(issues, CharacterValidationSeverity.Error, formation, "Usability", "Formation is disabled for both combat and exploration.");

            var slots = formation.Slots ?? new List<FormationSlot>();
            if (slots.Count == 0)
                Add(issues, CharacterValidationSeverity.Warning, formation, "Slots", "Formation has no slots.");

            var indices = new HashSet<int>();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, formation, $"Slots[{i}]", "Formation contains an empty slot.");
                    continue;
                }

                if (slot.SlotIndex < 0)
                    Add(issues, CharacterValidationSeverity.Error, formation, $"Slots[{i}].SlotIndex", "Formation slot index cannot be negative.");
                if (!indices.Add(slot.SlotIndex))
                    Add(issues, CharacterValidationSeverity.Error, formation, $"Slots[{i}].SlotIndex", $"Duplicate formation slot index {slot.SlotIndex}.");
                if (slot.DefenseModifier < -0.5f || slot.DefenseModifier > 0.5f)
                    Add(issues, CharacterValidationSeverity.Error, formation, $"Slots[{i}].DefenseModifier", "Defense modifier must stay within [-0.5, 0.5].");
                if (slot.AttackModifier < -0.5f || slot.AttackModifier > 0.5f)
                    Add(issues, CharacterValidationSeverity.Error, formation, $"Slots[{i}].AttackModifier", "Attack modifier must stay within [-0.5, 0.5].");
                if (slot.ThreatWeight < 0f || slot.ThreatWeight > 2f)
                    Add(issues, CharacterValidationSeverity.Error, formation, $"Slots[{i}].ThreatWeight", "Threat weight must stay within [0, 2].");
            }
        }

        private static void ValidateTalentNode(List<CharacterValidationIssue> issues, TalentNodeSO node)
        {
            if (node == null)
                return;

            RequireText(issues, node, "NodeId", node.NodeId, "Talent node must have a stable ID.");
            RequireText(issues, node, "DisplayName", node.DisplayName, "Talent node must have a display name.");
            RequireText(issues, node, "Description", node.Description, "Talent node must have a description.");
            if (node.MaxLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, node, "MaxLevel", "Talent node max level must be positive.");
            if (node.PointCostPerLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, node, "PointCostPerLevel", "Talent point cost must be positive.");
            if (node.PrerequisiteMinLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, node, "PrerequisiteMinLevel", "Prerequisite minimum level must be positive.");
            if (node.RequiredCharacterLevel <= 0)
                Add(issues, CharacterValidationSeverity.Error, node, "RequiredCharacterLevel", "Required character level must be positive.");

            ValidateTalentPrerequisites(issues, node);
            ValidateTalentEffects(issues, node);
        }

        private static void ValidateTalentTree(List<CharacterValidationIssue> issues, TalentTreeSO tree)
        {
            if (tree == null)
                return;

            RequireText(issues, tree, "TreeId", tree.TreeId, "Talent tree must have a stable ID.");
            RequireText(issues, tree, "DisplayName", tree.DisplayName, "Talent tree must have a display name.");
            RequireText(issues, tree, "Description", tree.Description, "Talent tree must have a description.");

            var nodes = tree.Nodes ?? new List<TalentNodeSO>();
            if (nodes.Count == 0)
                Add(issues, CharacterValidationSeverity.Error, tree, "Nodes", "Talent tree must contain at least one node.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, tree, $"Nodes[{i}]", "Talent tree contains an empty node reference.");
                    continue;
                }

                string id = Normalize(node.NodeId);
                if (string.IsNullOrEmpty(id))
                    Add(issues, CharacterValidationSeverity.Error, tree, $"Nodes[{i}].NodeId", "Talent tree node reference has an empty NodeId.");
                else if (!ids.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, tree, $"Nodes[{i}].NodeId", $"Duplicate talent node ID '{id}' in tree.");
            }

            if (tree.StartNode == null)
                Add(issues, CharacterValidationSeverity.Error, tree, "StartNode", "Talent tree must define a start node.");
            else if (!nodes.Contains(tree.StartNode))
                Add(issues, CharacterValidationSeverity.Error, tree, "StartNode", "Start node must be included in Nodes.");

            ValidateTalentConnections(issues, tree, ids);
            RunTalentTreeRuntimeValidation(issues, tree);
        }

        private static void ValidateStatModifierDataList(List<CharacterValidationIssue> issues, UnityEngine.Object asset, string fieldPath, List<StatModifierData> stats)
        {
            if (stats == null)
                return;

            for (int i = 0; i < stats.Count; i++)
            {
                var stat = stats[i];
                if (stat == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{i}]", "Stat modifier entry is empty.");
                    continue;
                }

                if (Mathf.Approximately(stat.BaseValue, 0f) && Mathf.Approximately(stat.ValuePerLevel, 0f))
                    Add(issues, CharacterValidationSeverity.Warning, asset, $"{fieldPath}[{i}]", "Stat modifier has no base value or per-level value.");
            }
        }

        private static void ValidateJobSkillReferences(List<CharacterValidationIssue> issues, JobDataSO job, string fieldPath, List<JobSkillSO> skills)
        {
            if (skills == null)
                return;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                if (skill == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, job, $"{fieldPath}[{i}]", "Job contains an empty skill reference.");
                    continue;
                }

                string id = Normalize(skill.SkillId);
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, job, $"{fieldPath}[{i}]", $"Duplicate skill ID '{id}' in job.");
            }
        }

        private static void ValidateJobPassiveReferences(List<CharacterValidationIssue> issues, JobDataSO job, string fieldPath, List<JobPassiveSO> passives)
        {
            if (passives == null)
                return;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < passives.Count; i++)
            {
                var passive = passives[i];
                if (passive == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, job, $"{fieldPath}[{i}]", "Job contains an empty passive reference.");
                    continue;
                }

                string id = Normalize(passive.PassiveId);
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, job, $"{fieldPath}[{i}]", $"Duplicate passive ID '{id}' in job.");
            }
        }

        private static void ValidateJobUnlockConditions(List<CharacterValidationIssue> issues, JobDataSO job, List<IJobUnlockCondition> conditions)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                string path = $"UnlockConditions[{i}]";
                if (condition == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, job, path, "Unlock condition is empty.");
                    continue;
                }

                switch (condition)
                {
                    case CharacterLevelCondition characterLevel when characterLevel.RequiredLevel <= 0:
                        Add(issues, CharacterValidationSeverity.Error, job, $"{path}.RequiredLevel", "Character level condition must require a positive level.");
                        break;
                    case JobLevelCondition jobLevel:
                        if (jobLevel.RequiredJob == JobType.None)
                            Add(issues, CharacterValidationSeverity.Error, job, $"{path}.RequiredJob", "Job level condition must reference a job.");
                        if (jobLevel.RequiredLevel <= 0)
                            Add(issues, CharacterValidationSeverity.Error, job, $"{path}.RequiredLevel", "Job level condition must require a positive level.");
                        break;
                    case MultiJobCondition multiJob:
                        ValidateJobLevelRequirements(issues, job, $"{path}.Requirements", multiJob.Requirements);
                        break;
                    case ItemRequiredCondition itemRequired when string.IsNullOrWhiteSpace(itemRequired.ItemId):
                        Add(issues, CharacterValidationSeverity.Error, job, $"{path}.ItemId", "Item unlock condition must reference an item ID.");
                        break;
                    case ShrineUnlockCondition shrine:
                        RequireText(issues, job, $"{path}.ShrineId", shrine.ShrineId, "Shrine unlock condition must reference a shrine ID.");
                        RequireText(issues, job, $"{path}.GuardianId", shrine.GuardianId, "Shrine unlock condition must reference a guardian ID.");
                        break;
                }
            }
        }

        private static void ValidateJobLevelRequirements(List<CharacterValidationIssue> issues, UnityEngine.Object asset, string fieldPath, List<JobLevelRequirement> requirements)
        {
            if (requirements == null || requirements.Count == 0)
            {
                Add(issues, CharacterValidationSeverity.Error, asset, fieldPath, "Multi-job condition must include at least one job requirement.");
                return;
            }

            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                if (requirement == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{i}]", "Job level requirement is empty.");
                    continue;
                }

                if (requirement.Job == JobType.None)
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{i}].Job", "Job level requirement must reference a job.");
                if (requirement.Level <= 0)
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{i}].Level", "Job level requirement must be positive.");
            }
        }

        private static void ValidateJobSkillConditions(List<CharacterValidationIssue> issues, JobSkillSO skill, List<IJobSkillCondition> conditions)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                string path = $"UnlockConditions[{i}]";
                if (condition == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, skill, path, "Skill unlock condition is empty.");
                    continue;
                }

                switch (condition)
                {
                    case QuestCompleteCondition quest when string.IsNullOrWhiteSpace(quest.QuestId):
                        Add(issues, CharacterValidationSeverity.Error, skill, $"{path}.QuestId", "Quest condition must reference a quest ID.");
                        break;
                    case RelationshipCondition relationship:
                        if (string.IsNullOrWhiteSpace(relationship.NpcId))
                            Add(issues, CharacterValidationSeverity.Error, skill, $"{path}.NpcId", "Relationship condition must reference an NPC ID.");
                        if (relationship.RequiredLevel <= 0)
                            Add(issues, CharacterValidationSeverity.Error, skill, $"{path}.RequiredLevel", "Relationship condition must require a positive level.");
                        break;
                    case DefeatEnemyCondition defeat when defeat.RequiredCount <= 0:
                        Add(issues, CharacterValidationSeverity.Error, skill, $"{path}.RequiredCount", "Defeat enemy condition must require a positive count.");
                        break;
                }
            }
        }

        private static void ValidateSkillStatModifiers(List<CharacterValidationIssue> issues, UnityEngine.Object asset, string fieldPath, List<SkillStatModifier> modifiers)
        {
            if (modifiers == null)
                return;

            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                if (modifier == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{i}]", "Skill stat modifier is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(modifier.StatName))
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{i}].StatName", "Skill stat modifier must name a stat.");
                if (Mathf.Approximately(modifier.Value, 0f))
                    Add(issues, CharacterValidationSeverity.Warning, asset, $"{fieldPath}[{i}].Value", "Skill stat modifier has a zero value.");
            }
        }

        private static void ValidateJobDatabaseList(List<CharacterValidationIssue> issues, JobDatabaseSO database, string fieldPath, List<JobDataSO> jobs, List<JobDataSO> destination)
        {
            if (jobs == null)
                return;

            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (job == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, database, $"{fieldPath}[{i}]", "Job database contains an empty job reference.");
                    continue;
                }

                destination.Add(job);
            }
        }

        private static void ValidateMartialSkillEntries(List<CharacterValidationIssue> issues, MartialArtDataSO art)
        {
            var skills = art.skills ?? new List<MartialSkillEntry>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < skills.Count; i++)
            {
                var skill = skills[i];
                if (skill == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, art, $"skills[{i}]", "Martial art skill entry is empty.");
                    continue;
                }

                RequireText(issues, art, $"skills[{i}].skillId", skill.skillId, "Martial art skill must have an ID.");
                RequireText(issues, art, $"skills[{i}].skillName", skill.skillName, "Martial art skill must have a display name.");
                if (skill.unlockLevel <= 0)
                    Add(issues, CharacterValidationSeverity.Error, art, $"skills[{i}].unlockLevel", "Martial art skill unlock level must be positive.");
                if (art.maxLevel > 0 && skill.unlockLevel > art.maxLevel)
                    Add(issues, CharacterValidationSeverity.Error, art, $"skills[{i}].unlockLevel", "Martial art skill unlock level exceeds max level.");

                string id = Normalize(skill.skillId);
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, art, $"skills[{i}].skillId", $"Duplicate martial skill ID '{id}'.");
            }
        }

        private static void ValidateLearnRequirements(List<CharacterValidationIssue> issues, MartialArtDataSO art)
        {
            var requirements = art.requirements ?? new List<LearnRequirement>();
            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                if (requirement == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, art, $"requirements[{i}]", "Learn requirement is empty.");
                    continue;
                }

                if (requirement.type == LearnRequirementType.None)
                    Add(issues, CharacterValidationSeverity.Warning, art, $"requirements[{i}].type", "Learn requirement type is None.");
                RequireText(issues, art, $"requirements[{i}].description", requirement.description, "Learn requirement should describe the condition.", CharacterValidationSeverity.Warning);
                if ((requirement.type & (LearnRequirementType.Realm | LearnRequirementType.Stat)) != 0 && requirement.valueRequired <= 0)
                    Add(issues, CharacterValidationSeverity.Error, art, $"requirements[{i}].valueRequired", "Numeric learn requirement must be positive.");
                if ((requirement.type & (LearnRequirementType.PrerequisiteArt | LearnRequirementType.Sect | LearnRequirementType.Item)) != 0
                    && string.IsNullOrWhiteSpace(requirement.stringRequired))
                    Add(issues, CharacterValidationSeverity.Error, art, $"requirements[{i}].stringRequired", "Reference-based learn requirement must provide an ID.");
            }
        }

        private static void ValidateBreakthroughRequirements(List<CharacterValidationIssue> issues, RealmDataSO realm)
        {
            var requirements = realm.requirements ?? new List<BreakthroughRequirement>();
            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                if (requirement == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, realm, $"requirements[{i}]", "Breakthrough requirement is empty.");
                    continue;
                }

                RequireText(issues, realm, $"requirements[{i}].description", requirement.description, "Breakthrough requirement should describe the condition.", CharacterValidationSeverity.Warning);
                if (requirement.valueRequired < 0)
                    Add(issues, CharacterValidationSeverity.Error, realm, $"requirements[{i}].valueRequired", "Breakthrough requirement value cannot be negative.");
                if ((requirement.type == BreakthroughRequirementType.Item
                     || requirement.type == BreakthroughRequirementType.MartialArt
                     || requirement.type == BreakthroughRequirementType.Quest)
                    && string.IsNullOrWhiteSpace(requirement.stringRequired))
                    Add(issues, CharacterValidationSeverity.Error, realm, $"requirements[{i}].stringRequired", "Reference-based breakthrough requirement must provide an ID.");
            }
        }

        private static void ValidateSectMartialArts(List<CharacterValidationIssue> issues, SectDataSO sect)
        {
            var entries = sect.martialArts ?? new List<SectMartialArtEntry>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, sect, $"martialArts[{i}]", "Sect martial art entry is empty.");
                    continue;
                }

                RequireText(issues, sect, $"martialArts[{i}].martialArtId", entry.martialArtId, "Sect martial art entry must reference a martial art ID.");
                RequireText(issues, sect, $"martialArts[{i}].displayName", entry.displayName, "Sect martial art entry must have a display name.");
                if (entry.accessLevel == SectMartialArtAccess.None)
                    Add(issues, CharacterValidationSeverity.Error, sect, $"martialArts[{i}].accessLevel", "Sect martial art access cannot be None.");
                if (entry.requiredRank == SectRank.None)
                    Add(issues, CharacterValidationSeverity.Warning, sect, $"martialArts[{i}].requiredRank", "Sect martial art is available with no rank requirement.");
                if (entry.contributionCost < 0)
                    Add(issues, CharacterValidationSeverity.Error, sect, $"martialArts[{i}].contributionCost", "Sect martial art contribution cost cannot be negative.");

                string id = Normalize(entry.martialArtId);
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, sect, $"martialArts[{i}].martialArtId", $"Duplicate sect martial art ID '{id}'.");
            }
        }

        private static void ValidateRankRequirements(List<CharacterValidationIssue> issues, SectDataSO sect)
        {
            var requirements = sect.rankRequirements ?? new List<RankRequirement>();
            var ranks = new HashSet<SectRank>();
            for (int i = 0; i < requirements.Count; i++)
            {
                var requirement = requirements[i];
                if (requirement == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, sect, $"rankRequirements[{i}]", "Rank requirement is empty.");
                    continue;
                }

                if (!ranks.Add(requirement.rank))
                    Add(issues, CharacterValidationSeverity.Error, sect, $"rankRequirements[{i}].rank", $"Duplicate rank requirement for {requirement.rank}.");
                if (requirement.contributionRequired < 0)
                    Add(issues, CharacterValidationSeverity.Error, sect, $"rankRequirements[{i}].contributionRequired", "Contribution requirement cannot be negative.");
                if (requirement.realmLevelRequired < 0)
                    Add(issues, CharacterValidationSeverity.Error, sect, $"rankRequirements[{i}].realmLevelRequired", "Realm level requirement cannot be negative.");
                if (requirement.questsRequired < 0)
                    Add(issues, CharacterValidationSeverity.Error, sect, $"rankRequirements[{i}].questsRequired", "Quest requirement cannot be negative.");
            }
        }

        private static void ValidateSectRelationships(List<CharacterValidationIssue> issues, SectDataSO sect)
        {
            ValidateEnumList(issues, sect, "friendlySects", sect.friendlySects, SectType.None);
            ValidateEnumList(issues, sect, "hostileSects", sect.hostileSects, SectType.None);

            var friendly = new HashSet<SectType>(sect.friendlySects ?? new List<SectType>());
            foreach (var hostile in sect.hostileSects ?? new List<SectType>())
            {
                if (hostile != SectType.None && friendly.Contains(hostile))
                    Add(issues, CharacterValidationSeverity.Error, sect, "hostileSects", $"Sect {hostile} cannot be both friendly and hostile.");
            }
        }

        private static void ValidateTalentPrerequisites(List<CharacterValidationIssue> issues, TalentNodeSO node)
        {
            var prerequisites = node.Prerequisites ?? new List<TalentNodeSO>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < prerequisites.Count; i++)
            {
                var prerequisite = prerequisites[i];
                if (prerequisite == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, node, $"Prerequisites[{i}]", "Talent prerequisite is empty.");
                    continue;
                }

                if (prerequisite == node)
                    Add(issues, CharacterValidationSeverity.Error, node, $"Prerequisites[{i}]", "Talent node cannot require itself.");

                string id = Normalize(prerequisite.NodeId);
                if (!string.IsNullOrEmpty(id) && !ids.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, node, $"Prerequisites[{i}]", $"Duplicate prerequisite node ID '{id}'.");
            }
        }

        private static void ValidateTalentEffects(List<CharacterValidationIssue> issues, TalentNodeSO node)
        {
            var effects = node.Effects ?? new List<TalentEffect>();
            if (effects.Count == 0)
                Add(issues, CharacterValidationSeverity.Warning, node, "Effects", "Talent node has no effects.");

            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                string path = $"Effects[{i}]";
                if (effect == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, node, path, "Talent effect is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(effect.Description))
                    Add(issues, CharacterValidationSeverity.Warning, node, $"{path}.Description", "Talent effect should have a designer-facing description.");

                switch (effect)
                {
                    case StatModifierEffect stat:
                        if (Mathf.Approximately(stat.BaseValue, 0f) && Mathf.Approximately(stat.ValuePerLevel, 0f))
                            Add(issues, CharacterValidationSeverity.Warning, node, path, "Stat modifier effect has no value.");
                        break;
                    case MultiStatModifierEffect multi:
                        ValidateMultiStatEffect(issues, node, path, multi);
                        break;
                    case BuffEffect buff:
                        RequireText(issues, node, $"{path}.BuffId", buff.BuffId, "Buff effect must reference a Buff ID.");
                        if (buff.BaseStacks < 0 || buff.StacksPerLevel < 0)
                            Add(issues, CharacterValidationSeverity.Error, node, path, "Buff stacks cannot be negative.");
                        if (buff.BaseStacks == 0 && buff.StacksPerLevel == 0)
                            Add(issues, CharacterValidationSeverity.Error, node, path, "Buff effect must grant at least one stack.");
                        break;
                    case UnlockAbilityEffect ability:
                        RequireText(issues, node, $"{path}.AbilityId", ability.AbilityId, "Ability effect must reference an ability ID.");
                        if (ability.AbilityLevelPerTalentLevel <= 0)
                            Add(issues, CharacterValidationSeverity.Error, node, $"{path}.AbilityLevelPerTalentLevel", "Ability level per talent level must be positive.");
                        break;
                    case CustomEffect custom:
                        RequireText(issues, node, $"{path}.CallbackId", custom.CallbackId, "Custom effect must reference a callback ID.");
                        break;
                }
            }
        }

        private static void ValidateMultiStatEffect(List<CharacterValidationIssue> issues, TalentNodeSO node, string path, MultiStatModifierEffect multi)
        {
            if (multi.Stats == null || multi.Stats.Count == 0)
            {
                Add(issues, CharacterValidationSeverity.Error, node, $"{path}.Stats", "Multi-stat effect must contain at least one stat entry.");
                return;
            }

            for (int i = 0; i < multi.Stats.Count; i++)
            {
                var stat = multi.Stats[i];
                if (stat == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, node, $"{path}.Stats[{i}]", "Multi-stat effect entry is empty.");
                    continue;
                }

                if (Mathf.Approximately(stat.BaseValue, 0f) && Mathf.Approximately(stat.ValuePerLevel, 0f))
                    Add(issues, CharacterValidationSeverity.Warning, node, $"{path}.Stats[{i}]", "Multi-stat entry has no value.");
            }
        }

        private static void ValidateTalentConnections(List<CharacterValidationIssue> issues, TalentTreeSO tree, HashSet<string> nodeIds)
        {
            var connections = tree.Connections ?? new List<TalentConnection>();
            for (int i = 0; i < connections.Count; i++)
            {
                var connection = connections[i];
                if (connection == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, tree, $"Connections[{i}]", "Talent connection is empty.");
                    continue;
                }

                string from = Normalize(connection.FromNodeId);
                string to = Normalize(connection.ToNodeId);
                RequireText(issues, tree, $"Connections[{i}].FromNodeId", from, "Talent connection must define a source node.");
                RequireText(issues, tree, $"Connections[{i}].ToNodeId", to, "Talent connection must define a target node.");

                if (!string.IsNullOrEmpty(from) && !nodeIds.Contains(from))
                    Add(issues, CharacterValidationSeverity.Error, tree, $"Connections[{i}].FromNodeId", $"Connection references missing node '{from}'.");
                if (!string.IsNullOrEmpty(to) && !nodeIds.Contains(to))
                    Add(issues, CharacterValidationSeverity.Error, tree, $"Connections[{i}].ToNodeId", $"Connection references missing node '{to}'.");
                if (!string.IsNullOrEmpty(from) && string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                    Add(issues, CharacterValidationSeverity.Warning, tree, $"Connections[{i}]", "Talent connection points to itself.");
            }
        }

        private static void RunTalentTreeRuntimeValidation(List<CharacterValidationIssue> issues, TalentTreeSO tree)
        {
            if ((tree.Nodes != null && tree.Nodes.Any(node => node == null))
                || (tree.Connections != null && tree.Connections.Any(connection => connection == null)))
            {
                return;
            }

            try
            {
                if (!tree.Validate(out var errors))
                {
                    foreach (var error in errors)
                    {
                        Add(issues, CharacterValidationSeverity.Error, tree, "Validate()", error);
                    }
                }
            }
            catch (Exception exception)
            {
                Add(issues, CharacterValidationSeverity.Error, tree, "Validate()", $"Runtime validation threw {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static void ValidateStringIds(
            List<CharacterValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            IEnumerable<string> ids,
            Func<string, string> extraError = null,
            CharacterValidationSeverity emptySeverity = CharacterValidationSeverity.Error)
        {
            if (ids == null)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (var rawId in ids)
            {
                string id = Normalize(rawId);
                if (string.IsNullOrEmpty(id))
                {
                    Add(issues, emptySeverity, asset, $"{fieldPath}[{index}]", "ID entry is empty.");
                    index++;
                    continue;
                }

                if (!seen.Add(id))
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{index}]", $"Duplicate ID '{id}'.");

                string extra = extraError?.Invoke(id);
                if (!string.IsNullOrEmpty(extra))
                    Add(issues, CharacterValidationSeverity.Error, asset, $"{fieldPath}[{index}]", extra);

                index++;
            }
        }

        private static void ValidateEnumList<TEnum>(
            List<CharacterValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            IEnumerable<TEnum> values,
            TEnum invalidValue)
            where TEnum : struct, Enum
        {
            if (values == null)
                return;

            var seen = new HashSet<TEnum>();
            int index = 0;
            foreach (var value in values)
            {
                if (EqualityComparer<TEnum>.Default.Equals(value, invalidValue))
                    Add(issues, CharacterValidationSeverity.Warning, asset, $"{fieldPath}[{index}]", $"{fieldPath} contains {invalidValue}.");
                if (!seen.Add(value))
                    Add(issues, CharacterValidationSeverity.Warning, asset, $"{fieldPath}[{index}]", $"Duplicate enum value '{value}'.");
                index++;
            }
        }

        private static void ValidateDatabaseReferences<T>(
            List<CharacterValidationIssue> issues,
            UnityEngine.Object database,
            string fieldPath,
            IReadOnlyList<T> entries,
            Func<T, string> getKey,
            string keyLabel)
            where T : UnityEngine.Object
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    Add(issues, CharacterValidationSeverity.Error, database, $"{fieldPath}[{i}]", "Database contains an empty reference.");
                    continue;
                }

                string key = Normalize(getKey(entry));
                if (string.IsNullOrEmpty(key))
                {
                    Add(issues, CharacterValidationSeverity.Error, database, $"{fieldPath}[{i}]", $"Referenced {keyLabel} is empty.");
                    continue;
                }

                if (!seen.Add(key))
                    Add(issues, CharacterValidationSeverity.Error, database, $"{fieldPath}[{i}]", $"Duplicate {keyLabel} '{key}'.");
            }
        }

        private static void ValidatePercent(List<CharacterValidationIssue> issues, UnityEngine.Object asset, string fieldPath, int value)
        {
            if (value < 0 || value > 100)
                Add(issues, CharacterValidationSeverity.Error, asset, fieldPath, "Percent value must be between 0 and 100.");
        }

        private static void AddDuplicateStringIssues<T>(
            List<CharacterValidationIssue> issues,
            IEnumerable<T> assets,
            Func<T, string> getId,
            string fieldPath,
            string label)
            where T : UnityEngine.Object
        {
            var seen = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.Where(asset => asset != null))
            {
                string id = Normalize(getId(asset));
                if (string.IsNullOrEmpty(id))
                    continue;

                if (seen.ContainsKey(id))
                    Add(issues, CharacterValidationSeverity.Error, asset, fieldPath, $"{label} '{id}' is duplicated.");
                else
                    seen[id] = asset;
            }
        }

        private static void AddDuplicateEnumIssues<T, TEnum>(
            List<CharacterValidationIssue> issues,
            IEnumerable<T> assets,
            Func<T, TEnum> getValue,
            TEnum ignoredValue,
            string fieldPath,
            string label,
            UnityEngine.Object owner = null)
            where T : UnityEngine.Object
            where TEnum : struct, Enum
        {
            var seen = new Dictionary<TEnum, T>();
            foreach (var asset in assets.Where(asset => asset != null))
            {
                var value = getValue(asset);
                if (EqualityComparer<TEnum>.Default.Equals(value, ignoredValue))
                    continue;

                if (seen.ContainsKey(value))
                    Add(issues, CharacterValidationSeverity.Error, owner == null ? asset : owner, fieldPath, $"{label} '{value}' is duplicated.");
                else
                    seen[value] = asset;
            }
        }

        private static void RequireText(
            List<CharacterValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            string value,
            string message,
            CharacterValidationSeverity severity = CharacterValidationSeverity.Error)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(issues, severity, asset, fieldPath, message);
            else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
                Add(issues, CharacterValidationSeverity.Warning, asset, fieldPath, "Text contains leading or trailing whitespace.");
        }

        private static bool IsNormalizedRate(float value)
        {
            return value >= 0f && value <= 1f;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static void Add(
            List<CharacterValidationIssue> issues,
            CharacterValidationSeverity severity,
            UnityEngine.Object asset,
            string fieldPath,
            string message)
        {
            issues.Add(new CharacterValidationIssue(severity, GetAssetName(asset), fieldPath, message));
        }

        private static string GetAssetName(UnityEngine.Object asset)
        {
            if (asset == null)
                return "<null>";

            return string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name;
        }
    }
}
