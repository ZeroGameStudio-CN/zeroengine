// ============================================================================
// IMartialArtist.cs
// 习武者接口
// 创建于: 2026-01-09
// ============================================================================

using System.Collections.Generic;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>
    /// 习武者接口
    /// 任何可以学习武学的实体都应实现此接口
    /// </summary>
    public interface IMartialArtist
    {
        /// <summary>角色唯一 ID</summary>
        string CharacterId { get; }

        /// <summary>已学习的武学列表</summary>
        IReadOnlyList<MartialArtInstance> LearnedArts { get; }

        /// <summary>当前装备的主修内功</summary>
        MartialArtInstance ActiveInnerArt { get; }

        /// <summary>当前装备的主修外功</summary>
        MartialArtInstance ActiveOuterArt { get; }

        /// <summary>当前装备的轻功</summary>
        MartialArtInstance ActiveLightness { get; }

        /// <summary>
        /// 学习武学
        /// </summary>
        bool LearnMartialArt(MartialArtDataSO artData);

        /// <summary>
        /// 检查是否已学习指定武学
        /// </summary>
        bool HasLearnedMartialArt(string artId);

        /// <summary>
        /// 获取武学实例
        /// </summary>
        MartialArtInstance GetMartialArt(string artId);

        /// <summary>
        /// 装备武学到指定槽位
        /// </summary>
        bool EquipMartialArt(string artId, MartialArtSlotType slot);

        /// <summary>
        /// 卸下指定槽位的武学
        /// </summary>
        bool UnequipMartialArt(MartialArtSlotType slot);

        /// <summary>
        /// 增加武学经验
        /// </summary>
        void AddMartialArtExp(string artId, int amount);
    }
    /// <summary>Read-only actor-specific facts required by authored learning conditions.</summary>
    public interface IMartialArtRequirementQuery
    {
        bool IsRequirementMet(LearnRequirement requirement);
    }
}
