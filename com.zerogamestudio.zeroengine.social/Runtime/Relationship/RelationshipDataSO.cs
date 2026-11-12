using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Relationship
{
    /// <summary>
    /// NPC好感度数据 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "New NPC Relationship", menuName = "ZeroEngine/Relationship/NPC Data")]
    public class RelationshipDataSO : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("NPC唯一ID")]
        public string NpcId;

        [Tooltip("显示名称")]
        public string DisplayName;

        [Tooltip("描述/介绍")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("头像")]
        public Sprite Portrait;

        [Tooltip("NPC类型")]
        public NpcType NpcType = NpcType.Normal;

        [Header("好感度设置")]
        [Tooltip("初始好感度")]
        public int InitialPoints = 0;

        [Tooltip("每日好感度衰减")]
        public int DailyDecay = 0;

        [Tooltip("每日最大送礼次数")]
        public int MaxGiftsPerDay = 1;

        [Tooltip("每日最大对话次数（获得好感度）")]
        public int MaxTalksPerDay = 3;

        [Tooltip("对话获得好感度")]
        public int TalkPoints = 5;

        [Header("等级阈值")]
        [Tooltip("好感度等级配置")]
        public List<RelationshipThreshold> Thresholds = new List<RelationshipThreshold>();

        [Header("礼物偏好")]
        [Tooltip("喜欢的礼物")]
        public List<GiftData> LikedGifts = new List<GiftData>();

        [Tooltip("讨厌的礼物")]
        public List<GiftData> DislikedGifts = new List<GiftData>();

        [Tooltip("默认礼物好感度")]
        public int DefaultGiftPoints = 5;

        [Header("特殊事件")]
        [Tooltip("好感度事件")]
        public List<RelationshipEvent> Events = new List<RelationshipEvent>();

        [Header("高级设置")]
        [Tooltip("排序优先级")]
        public int SortOrder;

        [Tooltip("标签")]
        public List<string> Tags = new List<string>();

        /// <summary>
        /// 根据点数获取等级
        /// </summary>
        public RelationshipLevel GetLevelForPoints(int points)
        {
            RelationshipLevel result = RelationshipLevel.Stranger;

            for (int i = 0; i < Thresholds.Count; i++)
            {
                if (points >= Thresholds[i].RequiredPoints)
                {
                    result = Thresholds[i].Level;
                }
            }

            return result;
        }

        /// <summary>
        /// 获取升级所需点数
        /// </summary>
        public int GetPointsForNextLevel(RelationshipLevel currentLevel)
        {
            int nextLevelValue = (int)currentLevel + 1;

            for (int i = 0; i < Thresholds.Count; i++)
            {
                if ((int)Thresholds[i].Level == nextLevelValue)
                {
                    return Thresholds[i].RequiredPoints;
                }
            }

            return int.MaxValue;
        }

        /// <summary>
        /// 获取当前等级阈值
        /// </summary>
        public RelationshipThreshold GetThreshold(RelationshipLevel level)
        {
            for (int i = 0; i < Thresholds.Count; i++)
            {
                if (Thresholds[i].Level == level)
                {
                    return Thresholds[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 获取礼物偏好
        /// </summary>
        public GiftData GetGiftPreference(Inventory.InventoryItemSO item)
        {
            if (item == null) return null;

            // 检查喜欢的礼物
            for (int i = 0; i < LikedGifts.Count; i++)
            {
                if (LikedGifts[i]?.Item != null && !string.IsNullOrWhiteSpace(item.Id) && LikedGifts[i].Item.Id == item.Id)
                {
                    return LikedGifts[i];
                }
            }

            // 检查讨厌的礼物
            for (int i = 0; i < DislikedGifts.Count; i++)
            {
                if (DislikedGifts[i]?.Item != null && !string.IsNullOrWhiteSpace(item.Id) && DislikedGifts[i].Item.Id == item.Id)
                {
                    return DislikedGifts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 计算礼物好感度变化
        /// </summary>
        public int CalculateGiftPoints(Inventory.InventoryItemSO item, out GiftPreference preference)
        {
            var giftData = GetGiftPreference(item);

            if (giftData != null)
            {
                preference = giftData.Preference;
                return giftData.PointsChange;
            }

            preference = GiftPreference.Neutral;
            return DefaultGiftPoints;
        }

        /// <summary>
        /// 获取可触发的事件
        /// </summary>
        public void GetAvailableEvents(RelationshipProgress progress, List<RelationshipEvent> results)
        {
            results.Clear();

            for (int i = 0; i < Events.Count; i++)
            {
                var evt = Events[i];

                // 检查是否已触发
                if (evt.OneTime && progress.TriggeredEvents.Contains(evt.EventId))
                {
                    continue;
                }

                // 检查等级
                if ((int)progress.Level < (int)evt.RequiredLevel)
                {
                    continue;
                }

                // 检查点数
                if (progress.Points < evt.RequiredPoints)
                {
                    continue;
                }

                results.Add(evt);
            }
        }

        /// <summary>
        /// 是否有指定标签
        /// </summary>
        public bool HasTag(string tag)
        {
            if (Tags == null || string.IsNullOrEmpty(tag)) return false;
            for (int i = 0; i < Tags.Count; i++)
            {
                if (Tags[i] == tag) return true;
            }
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(NpcId))
            {
                NpcId = name;
            }
            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = name;
            }

            // 确保阈值按等级排序
            if (Thresholds != null)
            {
                Thresholds.Sort((a, b) => a.RequiredPoints.CompareTo(b.RequiredPoints));
            }
        }

        private void Reset()
        {
            // 默认阈值
            Thresholds = new List<RelationshipThreshold>
            {
                new RelationshipThreshold { Level = RelationshipLevel.Stranger, RequiredPoints = 0 },
                new RelationshipThreshold { Level = RelationshipLevel.Acquaintance, RequiredPoints = 50 },
                new RelationshipThreshold { Level = RelationshipLevel.Friend, RequiredPoints = 150 },
                new RelationshipThreshold { Level = RelationshipLevel.CloseFriend, RequiredPoints = 300 },
                new RelationshipThreshold { Level = RelationshipLevel.BestFriend, RequiredPoints = 500 }
            };
        }
#endif
    }

    /// <summary>
    /// NPC组（用于分类或阵营）
    /// </summary>
    [CreateAssetMenu(fileName = "New NPC Group", menuName = "ZeroEngine/Relationship/NPC Group")]
    public class RelationshipGroupSO : ScriptableObject
    {
        [Tooltip("组ID")]
        public string GroupId;

        [Tooltip("组名称")]
        public string DisplayName;

        [Tooltip("描述")]
        [TextArea(2, 3)]
        public string Description;

        [Tooltip("图标")]
        public Sprite Icon;

        [Tooltip("组内NPC")]
        public List<RelationshipDataSO> Members = new List<RelationshipDataSO>();

        [Tooltip("排序优先级")]
        public int SortOrder;
    }
}