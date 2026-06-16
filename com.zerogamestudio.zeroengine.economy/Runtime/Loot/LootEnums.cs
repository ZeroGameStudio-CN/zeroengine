using System;
using ZeroEngine.Economy;

namespace ZeroEngine.Loot
{
    /// <summary>
    /// 掉落模式
    /// </summary>
    public enum LootDropMode
    {
        /// <summary>纯权重随机</summary>
        Weight,

        /// <summary>保底模式（累计概率）</summary>
        Pity,

        /// <summary>分层抽取（先品质再物品）</summary>
        Layered
    }

    /// <summary>
    /// 掉落条目类型
    /// </summary>
    public enum LootEntryType
    {
        /// <summary>物品</summary>
        Item,

        /// <summary>嵌套掉落表</summary>
        Table,

        /// <summary>货币</summary>
        Currency,

        /// <summary>无（空掉落）</summary>
        Nothing
    }

    /// <summary>
    /// 掉落事件类型
    /// </summary>
    public enum LootEventType
    {
        /// <summary>掉落结果生成</summary>
        Rolled,

        /// <summary>保底触发</summary>
        PityTriggered,

        /// <summary>物品发放</summary>
        Granted
    }

    /// <summary>
    /// 掉落结果
    /// </summary>
    [Serializable]
    public struct LootResult
    {
        /// <summary>条目类型</summary>
        public LootEntryType Type;

        /// <summary>物品引用（Type=Item时有效）</summary>
        public Inventory.InventoryItemSO Item;

        /// <summary>货币类型（Type=Currency时有效）</summary>
        public CurrencyType Currency;

        /// <summary>数量</summary>
        public int Amount;

        /// <summary>是否为保底掉落</summary>
        public bool WasPityDrop;

        /// <summary>来源掉落表</summary>
        public LootTableSO SourceTable;

        /// <summary>来源条目索引</summary>
        public int SourceEntryIndex;

        public static LootResult Empty => new LootResult { Type = LootEntryType.Nothing };

        public static LootResult FromItem(Inventory.InventoryItemSO item, int amount, bool pity = false)
        {
            return new LootResult
            {
                Type = LootEntryType.Item,
                Item = item,
                Amount = amount,
                WasPityDrop = pity
            };
        }

        public static LootResult FromCurrency(CurrencyType currency, int amount, bool pity = false)
        {
            return new LootResult
            {
                Type = LootEntryType.Currency,
                Currency = currency,
                Amount = amount,
                WasPityDrop = pity
            };
        }
    }

    /// <summary>
    /// 货币类型（可扩展）
    /// </summary>
    public enum CurrencyType
    {
        Gold,
        Gem,
        Token,
        Experience
    }

    /// <summary>
    /// 掉落上下文（用于条件判断）
    /// </summary>
    [Serializable]
    public class LootContext
    {
        /// <summary>玩家等级</summary>
        public int PlayerLevel;

        /// <summary>掉落来源（怪物ID、宝箱ID等）</summary>
        public string SourceId;

        /// <summary>自定义数据</summary>
        public System.Collections.Generic.Dictionary<string, object> CustomData;

        public IInventoryProvider InventoryProvider;
        public ICurrencyProvider CurrencyProvider;
        public IExternalSystemProvider ExternalSystemProvider;

        public LootContext()
        {
            CustomData = new System.Collections.Generic.Dictionary<string, object>();
        }

        public T GetCustom<T>(string key, T defaultValue = default)
        {
            if (CustomData != null && CustomData.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }
            return defaultValue;
        }

        public void SetCustom(string key, object value)
        {
            CustomData ??= new System.Collections.Generic.Dictionary<string, object>();
            CustomData[key] = value;
        }
    }

    /// <summary>
    /// 掉落事件参数
    /// </summary>
    public struct LootEventArgs
    {
        public LootEventType EventType;
        public LootTableSO Table;
        public LootResult[] Results;
        public LootContext Context;

        public static LootEventArgs Rolled(LootTableSO table, LootResult[] results, LootContext context)
        {
            return new LootEventArgs
            {
                EventType = LootEventType.Rolled,
                Table = table,
                Results = results,
                Context = context
            };
        }

        public static LootEventArgs PityTriggered(LootTableSO table, LootResult result)
        {
            return new LootEventArgs
            {
                EventType = LootEventType.PityTriggered,
                Table = table,
                Results = new[] { result }
            };
        }

        public static LootEventArgs Granted(LootTableSO table, LootResult[] results)
        {
            return new LootEventArgs
            {
                EventType = LootEventType.Granted,
                Table = table,
                Results = results
            };
        }
    }
}
