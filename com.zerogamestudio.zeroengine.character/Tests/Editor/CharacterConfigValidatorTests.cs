using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Character.Editor;
using ZeroEngine.Character.Job;
using ZeroEngine.Character.MartialArts;
using ZeroEngine.Character.Realm;
using ZeroEngine.Character.Sect;
using ZeroEngine.Equipment;
using ZeroEngine.Party;
using ZeroEngine.TalentTree;
using Object = UnityEngine.Object;

namespace ZeroEngine.Character.Editor.Tests
{
    public sealed class CharacterConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingCharacterConfigIssues()
        {
            var equipmentA = ScriptableObject.CreateInstance<EquipmentDataSO>();
            var equipmentB = ScriptableObject.CreateInstance<EquipmentDataSO>();
            var set = ScriptableObject.CreateInstance<EquipmentSetSO>();
            var slot = ScriptableObject.CreateInstance<EquipmentSlotType>();
            var job = ScriptableObject.CreateInstance<JobDataSO>();
            var skill = ScriptableObject.CreateInstance<JobSkillSO>();
            var passive = ScriptableObject.CreateInstance<JobPassiveSO>();
            var martialArt = ScriptableObject.CreateInstance<MartialArtDataSO>();
            var realm = ScriptableObject.CreateInstance<RealmDataSO>();
            var sect = ScriptableObject.CreateInstance<SectDataSO>();
            var party = ScriptableObject.CreateInstance<PartyConfigSO>();
            var formation = ScriptableObject.CreateInstance<FormationSO>();
            var node = ScriptableObject.CreateInstance<TalentNodeSO>();
            var tree = ScriptableObject.CreateInstance<TalentTreeSO>();

            var created = new Object[]
            {
                equipmentA,
                equipmentB,
                set,
                slot,
                job,
                skill,
                passive,
                martialArt,
                realm,
                sect,
                party,
                formation,
                node,
                tree
            };

            try
            {
                equipmentA.name = "EquipmentA";
                equipmentA.Id = "sword_001";
                equipmentA.ItemName = string.Empty;
                equipmentA.Description = string.Empty;
                equipmentA.SlotType = null;
                equipmentA.RequiredLevel = 0;
                equipmentA.BaseEnhanceSuccessRate = 2f;
                equipmentA.BaseStats.Add(new StatModifierData());

                equipmentB.name = "EquipmentB";
                equipmentB.Id = "sword_001";
                equipmentB.ItemName = "Duplicate Sword";
                equipmentB.Description = "Duplicate";
                equipmentB.SlotType = slot;

                set.name = "InvalidSet";
                set.SetId = "set_001";
                set.SetName = "Set";
                set.Description = "Set";
                set.Pieces.Add(equipmentA);
                set.Effects.Add(new SetEffect { RequiredPieces = 3, Description = string.Empty });

                slot.name = "InvalidSlot";
                slot.SlotId = "weapon";
                slot.DisplayName = string.Empty;
                slot.Description = string.Empty;
                slot.AllowedTags = new[] { "blade", "blade", string.Empty };

                job.name = "InvalidJob";
                job.JobType = JobType.None;
                job.DisplayName = string.Empty;
                job.Description = string.Empty;
                job.MaxJobLevel = 0;
                job.IsUnlockedByDefault = false;
                job.UnlockConditions.Add(new CharacterLevelCondition { RequiredLevel = 0 });

                skill.name = "InvalidSkill";
                skill.SkillId = "slash";
                skill.DisplayName = string.Empty;
                skill.Description = string.Empty;
                skill.RequiredJobLevel = 0;
                skill.JPCost = -1;
                skill.PrerequisiteSkillIds.Add("slash");
                skill.UnlockConditions.Add(new QuestCompleteCondition());

                passive.name = "InvalidPassive";
                passive.PassiveId = string.Empty;
                passive.RequiredJobLevel = 0;

                martialArt.name = "InvalidMartialArt";
                martialArt.artId = string.Empty;
                martialArt.artType = MartialArtType.None;
                martialArt.maxLevel = 3;
                martialArt.expPerLevel = new[] { 100, 0 };
                martialArt.cultivationSpeedMultiplier = 0f;
                martialArt.hasSideEffect = true;
                martialArt.requirements.Add(new LearnRequirement { type = LearnRequirementType.PrerequisiteArt });

                realm.name = "InvalidRealm";
                realm.realmType = RealmType.None;
                realm.realmName = string.Empty;
                realm.baseBreakthroughChance = 120;
                realm.requirements.Add(new BreakthroughRequirement { type = BreakthroughRequirementType.Item });

                sect.name = "InvalidSect";
                sect.sectType = SectType.None;
                sect.requiresUnlock = true;
                sect.minRealmLevel = -1;
                sect.friendlySects.Add(SectType.Shaolin);
                sect.hostileSects.Add(SectType.Shaolin);

                party.name = "InvalidParty";
                party.MaxActiveMembers = 0;
                party.MaxReserveMembers = -1;

                formation.name = "InvalidFormation";
                formation.FormationName = string.Empty;
                formation.SpacingScale = 0f;
                formation.RequiredPartyLevel = 0;
                formation.UsableInCombat = false;
                formation.UsableInExploration = false;
                formation.Slots.Add(new FormationSlot { SlotIndex = 0, ThreatWeight = 3f });
                formation.Slots.Add(new FormationSlot { SlotIndex = 0 });

                node.name = "InvalidNode";
                node.NodeId = "node_a";
                node.DisplayName = string.Empty;
                node.MaxLevel = 0;
                node.PointCostPerLevel = 0;
                node.Prerequisites.Add(node);
                node.Effects.Add(new BuffEffect { BuffId = string.Empty, BaseStacks = 0, StacksPerLevel = 0 });

                tree.name = "InvalidTree";
                tree.TreeId = "tree_a";
                tree.DisplayName = string.Empty;
                tree.Nodes.Add(node);
                tree.StartNode = null;
                tree.Connections.Add(new TalentConnection { FromNodeId = "node_a", ToNodeId = "missing_node" });

                var issues = CharacterConfigValidator.Validate(
                    equipment: new[] { equipmentA, equipmentB },
                    equipmentSets: new[] { set },
                    equipmentSlots: new[] { slot },
                    jobs: new[] { job },
                    jobSkills: new[] { skill },
                    jobPassives: new[] { passive },
                    jobDatabases: new JobDatabaseSO[0],
                    martialArts: new[] { martialArt },
                    martialArtDatabases: new MartialArtDatabaseSO[0],
                    realms: new[] { realm },
                    realmDatabases: new RealmDatabaseSO[0],
                    sects: new[] { sect },
                    sectDatabases: new SectDatabaseSO[0],
                    partyConfigs: new[] { party },
                    formations: new[] { formation },
                    talentNodes: new[] { node },
                    talentTrees: new[] { tree });

                Assert.That(issues.Count, Is.GreaterThan(20));
                AssertError(issues, "Equipment Id 'sword_001' is duplicated.");
                AssertError(issues, "Equipment must reference an equipment slot type.");
                AssertError(issues, "Required piece count exceeds the number of configured set pieces.");
                AssertError(issues, "Job type cannot be None.");
                AssertError(issues, "Skill cannot list itself as a prerequisite.");
                AssertError(issues, "Martial art type cannot be None.");
                AssertError(issues, "Percent value must be between 0 and 100.");
                AssertError(issues, "Sect Shaolin cannot be both friendly and hostile.");
                AssertError(issues, "Formation is disabled for both combat and exploration.");
                AssertError(issues, "Talent node cannot require itself.");
                AssertError(issues, "Talent tree must define a start node.");
                AssertError(issues, "Connection references missing node 'missing_node'.");
            }
            finally
            {
                foreach (var asset in created)
                    Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Validate_ReportsDatabaseDuplicateKeys()
        {
            var jobA = ScriptableObject.CreateInstance<JobDataSO>();
            var jobB = ScriptableObject.CreateInstance<JobDataSO>();
            var jobDatabase = ScriptableObject.CreateInstance<JobDatabaseSO>();
            var artA = ScriptableObject.CreateInstance<MartialArtDataSO>();
            var artB = ScriptableObject.CreateInstance<MartialArtDataSO>();
            var artDatabase = ScriptableObject.CreateInstance<MartialArtDatabaseSO>();
            var realmA = ScriptableObject.CreateInstance<RealmDataSO>();
            var realmB = ScriptableObject.CreateInstance<RealmDataSO>();
            var realmDatabase = ScriptableObject.CreateInstance<RealmDatabaseSO>();
            var sectA = ScriptableObject.CreateInstance<SectDataSO>();
            var sectB = ScriptableObject.CreateInstance<SectDataSO>();
            var sectDatabase = ScriptableObject.CreateInstance<SectDatabaseSO>();

            var created = new Object[]
            {
                jobA,
                jobB,
                jobDatabase,
                artA,
                artB,
                artDatabase,
                realmA,
                realmB,
                realmDatabase,
                sectA,
                sectB,
                sectDatabase
            };

            try
            {
                jobA.JobType = JobType.Warrior;
                jobB.JobType = JobType.Warrior;
                jobDatabase.BasicJobs.Add(jobA);
                jobDatabase.AdvancedJobs.Add(jobB);

                artA.artId = "inner_art";
                artB.artId = "inner_art";
                artDatabase.AddMartialArt(artA);
                artDatabase.AddMartialArt(artB);

                realmA.realmType = RealmType.Mortal_Beginner;
                realmB.realmType = RealmType.Mortal_Beginner;
                realmDatabase.AddRealm(realmA);
                realmDatabase.AddRealm(realmB);

                sectA.sectType = SectType.Shaolin;
                sectB.sectType = SectType.Shaolin;
                sectDatabase.AddSect(sectA);
                sectDatabase.AddSect(sectB);

                var issues = CharacterConfigValidator.Validate(
                    equipment: new EquipmentDataSO[0],
                    equipmentSets: new EquipmentSetSO[0],
                    equipmentSlots: new EquipmentSlotType[0],
                    jobs: new JobDataSO[0],
                    jobSkills: new JobSkillSO[0],
                    jobPassives: new JobPassiveSO[0],
                    jobDatabases: new[] { jobDatabase },
                    martialArts: new MartialArtDataSO[0],
                    martialArtDatabases: new[] { artDatabase },
                    realms: new RealmDataSO[0],
                    realmDatabases: new[] { realmDatabase },
                    sects: new SectDataSO[0],
                    sectDatabases: new[] { sectDatabase },
                    partyConfigs: new PartyConfigSO[0],
                    formations: new FormationSO[0],
                    talentNodes: new TalentNodeSO[0],
                    talentTrees: new TalentTreeSO[0]);

                AssertError(issues, "Job database contains duplicate job types 'Warrior' is duplicated.");
                AssertError(issues, "Duplicate martial art ID 'inner_art'.");
                AssertError(issues, "Duplicate realm type 'Mortal_Beginner'.");
                AssertError(issues, "Duplicate sect type 'Shaolin'.");
            }
            finally
            {
                foreach (var asset in created)
                    Object.DestroyImmediate(asset);
            }
        }

        private static void AssertError(IReadOnlyList<CharacterValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == CharacterValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
