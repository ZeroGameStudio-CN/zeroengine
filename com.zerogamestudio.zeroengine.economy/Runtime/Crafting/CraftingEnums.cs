using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Economy;

namespace ZeroEngine.Crafting
{
    /// <summary>
    /// 配方分类
    /// </summary>
    public enum RecipeCategory
    {
        /// <summary>武器</summary>
        Weapon,

        /// <summary>防具</summary>
        Armor,

        /// <summary>消耗品</summary>
        Consumable,

        /// <summary>材料</summary>
        Material,

        /// <summary>工具</summary>
        Tool,

        /// <summary>装饰</summary>
        Decoration,

        /// <summary>其他</summary>
        Other
    }

    /// <summary>
    /// 配方解锁方式
    /// </summary>
    public enum RecipeUnlockType
    {
        /// <summary>默认解锁</summary>
        Default,

        /// <summary>等级解锁</summary>
        Level,

        /// <summary>任务解锁</summary>
        Quest,

        /// <summary>成就解锁</summary>
        Achievement,

        /// <summary>物品解锁（学习配方）</summary>
        Item,

        /// <summary>好感度解锁</summary>
        Relationship,

        /// <summary>自定义条件</summary>
        Custom
    }

    /// <summary>
    /// 合成结果类型
    /// </summary>
    public enum CraftingResult
    {
        /// <summary>成功</summary>
        Success,

        /// <summary>失败（保留材料）</summary>
        FailedKeepMaterials,

        /// <summary>失败（消耗材料）</summary>
        FailedLoseMaterials,

        /// <summary>大成功（额外产出）</summary>
        GreatSuccess,

        /// <summary>材料不足</summary>
        InsufficientMaterials,

        /// <summary>配方未解锁</summary>
        RecipeLocked,

        /// <summary>工作台不匹配</summary>
        WrongWorkbench
    }

    /// <summary>
    /// 合成事件类型
    /// </summary>
    public enum CraftingEventType
    {
        /// <summary>开始合成</summary>
        Started,

        /// <summary>合成完成</summary>
        Completed,

        /// <summary>合成失败</summary>
        Failed,

        /// <summary>配方解锁</summary>
        RecipeUnlocked,

        /// <summary>技能升级</summary>
        SkillLevelUp
    }

    /// <summary>
    /// 配方材料
    /// </summary>
    [Serializable]
    public class RecipeIngredient
    {
        [Tooltip("物品")]
        public Inventory.InventoryItemSO Item;

        [Tooltip("数量")]
        public int Amount = 1;

        [Tooltip("是否消耗（false = 工具类，不消耗）")]
        public bool IsConsumed = true;

        /// <summary>
        /// 检查材料是否充足
        /// </summary>
        public bool Check(IInventoryProvider inventory)
        {
            if (Item == null) return true;
            return inventory != null && inventory.GetItemCount(Item) >= Amount;
        }

        /// <summary>
        /// 消耗材料
        /// </summary>
        public void Consume(IInventoryProvider inventory)
        {
            if (inventory == null || Item == null || !IsConsumed) return;
            inventory.RemoveItem(Item, Amount);
        }
    }

    /// <summary>
    /// 配方产出
    /// </summary>
    [Serializable]
    public class RecipeOutput
    {
        [Tooltip("产出物品")]
        public Inventory.InventoryItemSO Item;

        [Tooltip("基础数量")]
        public int BaseAmount = 1;

        [Tooltip("额外数量（大成功时）")]
        public int BonusAmount = 0;

        [Tooltip("产出概率（1.0 = 100%）")]
        public float Probability = 1f;
    }

    /// <summary>
    /// 工作台类型
    /// </summary>
    [Serializable]
    public class WorkbenchType
    {
        public string WorkbenchId;
        public string DisplayName;
        public Sprite Icon;
    }

    /// <summary>
    /// 合成进度数据
    /// </summary>
    [Serializable]
    public class CraftingProgress
    {
        /// <summary>配方ID</summary>
        public string RecipeId;

        /// <summary>开始时间</summary>
        public long StartTime;

        /// <summary>结束时间</summary>
        public long EndTime;

        /// <summary>批次数量</summary>
        public int BatchCount;

        /// <summary>是否完成</summary>
        public bool IsComplete => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= EndTime;

        /// <summary>剩余时间（秒）</summary>
        public float RemainingTime
        {
            get
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return Mathf.Max(0, EndTime - now);
            }
        }

        /// <summary>进度 (0-1)</summary>
        public float Progress
        {
            get
            {
                if (EndTime <= StartTime) return 1f;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                float total = EndTime - StartTime;
                float elapsed = now - StartTime;
                return Mathf.Clamp01(elapsed / total);
            }
        }
    }

    /// <summary>
    /// 合成事件参数
    /// </summary>
    public struct CraftingEventArgs
    {
        public CraftingEventType EventType;
        public CraftingRecipeSO Recipe;
        public CraftingResult Result;
        public List<RecipeOutput> Outputs;
        public int BatchCount;

        public static CraftingEventArgs Started(CraftingRecipeSO recipe, int batchCount)
        {
            return new CraftingEventArgs
            {
                EventType = CraftingEventType.Started,
                Recipe = recipe,
                BatchCount = batchCount
            };
        }

        public static CraftingEventArgs Completed(CraftingRecipeSO recipe, CraftingResult result, List<RecipeOutput> outputs)
        {
            return new CraftingEventArgs
            {
                EventType = CraftingEventType.Completed,
                Recipe = recipe,
                Result = result,
                Outputs = outputs
            };
        }

        public static CraftingEventArgs Failed(CraftingRecipeSO recipe, CraftingResult result)
        {
            return new CraftingEventArgs
            {
                EventType = CraftingEventType.Failed,
                Recipe = recipe,
                Result = result
            };
        }

        public static CraftingEventArgs RecipeUnlocked(CraftingRecipeSO recipe)
        {
            return new CraftingEventArgs
            {
                EventType = CraftingEventType.RecipeUnlocked,
                Recipe = recipe
            };
        }
    }

    /// <summary>
    /// 合成技能数据
    /// </summary>
    [Serializable]
    public class CraftingSkillData
    {
        /// <summary>技能ID</summary>
        public string SkillId;

        /// <summary>当前等级</summary>
        public int Level = 1;

        /// <summary>当前经验</summary>
        public int Experience;

        /// <summary>升级所需经验</summary>
        public int ExpToNextLevel => Level * 100;

        /// <summary>添加经验</summary>
        public bool AddExperience(int exp, out int levelUps)
        {
            levelUps = 0;
            Experience += exp;

            while (Experience >= ExpToNextLevel)
            {
                Experience -= ExpToNextLevel;
                Level++;
                levelUps++;
            }

            return levelUps > 0;
        }
    }
}
