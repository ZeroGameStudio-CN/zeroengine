// ============================================================================
// MartialArtDataSO.cs
// 武学数据配置 (ScriptableObject)
// 创建于: 2026-01-09
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>
    /// 武学数据配置
    /// 定义武学的基础信息、招式、修炼条件等
    /// </summary>
    [CreateAssetMenu(fileName = "New MartialArt", menuName = "ZeroEngine/Character/MartialArts/Martial Art Data")]
    public class MartialArtDataSO : ScriptableObject
    {
        [Header("基础信息")]
        [Tooltip("武学唯一 ID")]
        public string artId;

        [Tooltip("武学名称")]
        public string artName;

        [Tooltip("武学描述")]
        [TextArea(3, 6)]
        public string description;

        [Tooltip("武学类型")]
        public MartialArtType artType = MartialArtType.None;

        [Tooltip("武学品级")]
        public MartialArtGrade grade = MartialArtGrade.Medium;

        [Tooltip("五行属性")]
        public ElementType element = ElementType.None;

        [Tooltip("武学图标")]
        public Sprite icon;

        [Header("修炼配置")]
        [Tooltip("最大层数 (通常 10 层)")]
        [Range(1, 20)]
        public int maxLevel = 10;

        [Tooltip("每层所需经验")]
        public int[] expPerLevel = new int[10] { 100, 200, 400, 800, 1600, 3200, 6400, 12800, 25600, 51200 };

        [Tooltip("修炼速度倍率")]
        [Range(0.1f, 5f)]
        public float cultivationSpeedMultiplier = 1f;

        [Header("属性加成")]
        [Tooltip("每层属性加成")]
        public MartialArtStatBonus statBonusPerLevel;

        [Tooltip("满级额外加成")]
        public MartialArtStatBonus masteryBonus;

        [Header("招式列表")]
        [Tooltip("武学包含的招式")]
        public List<MartialSkillEntry> skills = new List<MartialSkillEntry>();

        [Header("学习条件")]
        [Tooltip("学习条件列表")]
        public List<LearnRequirement> requirements = new List<LearnRequirement>();

        [Header("特殊效果")]
        [Tooltip("被动效果描述")]
        [TextArea(2, 4)]
        public string passiveEffectDesc;

        [Tooltip("是否有副作用 (禁术)")]
        public bool hasSideEffect = false;

        [Tooltip("副作用描述")]
        [TextArea(2, 4)]
        public string sideEffectDesc;

        /// <summary>
        /// 获取指定层数所需经验
        /// </summary>
        public int GetExpForLevel(int level)
        {
            if (level <= 0 || level > maxLevel) return 0;
            if (level - 1 < expPerLevel.Length)
                return expPerLevel[level - 1];
            return expPerLevel[expPerLevel.Length - 1] * (level - expPerLevel.Length + 1);
        }

        /// <summary>
        /// 获取指定层数的属性加成
        /// </summary>
        public MartialArtStatBonus GetStatBonusAtLevel(int level)
        {
            if (level <= 0) return new MartialArtStatBonus();

            var bonus = new MartialArtStatBonus
            {
                health = statBonusPerLevel.health * level,
                energy = statBonusPerLevel.energy * level,
                attack = statBonusPerLevel.attack * level,
                defense = statBonusPerLevel.defense * level,
                speed = statBonusPerLevel.speed * level,
                innerDamage = statBonusPerLevel.innerDamage * level,
                outerDamage = statBonusPerLevel.outerDamage * level
            };

            // 满级额外加成
            if (level >= maxLevel)
            {
                bonus.health += masteryBonus.health;
                bonus.energy += masteryBonus.energy;
                bonus.attack += masteryBonus.attack;
                bonus.defense += masteryBonus.defense;
                bonus.speed += masteryBonus.speed;
                bonus.innerDamage += masteryBonus.innerDamage;
                bonus.outerDamage += masteryBonus.outerDamage;
            }

            return bonus;
        }

        /// <summary>
        /// 获取指定层数解锁的招式
        /// </summary>
        public List<MartialSkillEntry> GetSkillsAtLevel(int level)
        {
            return skills.FindAll(s => s.unlockLevel <= level);
        }

        /// <summary>
        /// 检查是否满足学习条件
        /// </summary>
        public bool CheckRequirements(IMartialArtist artist)
        {
            foreach (var req in requirements)
            {
                if (!req.IsMet(artist))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 武学属性加成
    /// </summary>
    [Serializable]
    public class MartialArtStatBonus
    {
        [Tooltip("生命值")]
        public float health = 0f;

        [Tooltip("内力/真气")]
        public float energy = 0f;

        [Tooltip("攻击力")]
        public float attack = 0f;

        [Tooltip("防御力")]
        public float defense = 0f;

        [Tooltip("速度")]
        public float speed = 0f;

        [Tooltip("内功伤害")]
        public float innerDamage = 0f;

        [Tooltip("外功伤害")]
        public float outerDamage = 0f;
    }

    /// <summary>
    /// 武学招式条目
    /// </summary>
    [Serializable]
    public class MartialSkillEntry
    {
        [Tooltip("招式 ID")]
        public string skillId;

        [Tooltip("招式名称")]
        public string skillName;

        [Tooltip("解锁层数")]
        [Range(1, 20)]
        public int unlockLevel = 1;

        [Tooltip("是否为绝招")]
        public bool isUltimate = false;
    }

    /// <summary>
    /// 学习条件
    /// </summary>
    [Serializable]
    public class LearnRequirement
    {
        [Tooltip("条件类型")]
        public LearnRequirementType type;

        [Tooltip("条件描述")]
        public string description;

        [Tooltip("数值要求 (境界等级/属性值等)")]
        public int valueRequired;

        [Tooltip("字符串要求 (武学 ID/门派 ID 等)")]
        public string stringRequired;

        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        public bool IsMet(IMartialArtist artist)
        {
            // Unknown and compound conditions cannot silently grant an authored requirement.
            switch (type)
            {
                case LearnRequirementType.None:
                    return true;
                case LearnRequirementType.PrerequisiteArt:
                    return !string.IsNullOrWhiteSpace(stringRequired)
                        && (artist?.HasLearnedMartialArt(stringRequired) ?? false);
                case LearnRequirementType.Realm:
                case LearnRequirementType.Stat:
                case LearnRequirementType.Sect:
                case LearnRequirementType.Item:
                case LearnRequirementType.Special:
                    return artist is IMartialArtRequirementQuery query && query.IsRequirementMet(this);
                default:
                    return false;
            }
        }
    }
}
