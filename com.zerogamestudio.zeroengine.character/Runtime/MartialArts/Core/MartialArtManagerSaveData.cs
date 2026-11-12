using System;

namespace ZeroEngine.Character.MartialArts
{
    /// <summary>
    /// 武学管理器存档数据
    /// </summary>
    [Serializable]
    public class MartialArtManagerSaveData
    {
        public string characterId;
        public MartialArtSaveData[] learnedArts;
        public EquippedSlotSaveData[] equippedSlots;
        // Required by the actor-owned contract; missing and explicitly empty have different meanings.
        public string[] preparedSkills;
    }

    /// <summary>
    /// 装备槽位存档数据
    /// </summary>
    [Serializable]
    public class EquippedSlotSaveData
    {
        public MartialArtSlotType slot;
        public string artId;
    }
}
