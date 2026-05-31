using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Currency;
using ZeroEngine.Economy;
using ZeroEngine.Save;

namespace ZeroEngine.Loot
{
    /// <summary>
    /// 掉落表管理器
    /// </summary>
    public class LootTableManager : MonoSingleton<LootTableManager>, ISaveable
    {
        [Header("配置")]
        [Tooltip("是否自动发放到背包")]
        [SerializeField] private bool _autoGrantToInventory = false;

        [Header("调试")]
        [SerializeField] private bool _debugMode;

        // 保底计数器 (TableId_EntryIndex -> count)
        private readonly Dictionary<string, int> _pityCounters = new Dictionary<string, int>();

        // 缓存列表（避免 GC）
        private readonly List<LootEntry> _tempValidEntries = new List<LootEntry>(32);
        private readonly List<LootResult> _tempResults = new List<LootResult>(16);
        private readonly List<int> _tempSelectedIndices = new List<int>(16);

        private IInventoryProvider _inventoryProvider;
        private ICurrencyProvider _currencyProvider;

        #region Events

        /// <summary>掉落事件</summary>
        public event Action<LootEventArgs> OnLootEvent;

        #endregion

        #region ISaveable

        public string SaveKey => "LootTable";

        public void Register()
        {
            SaveSlotManager.Instance?.Register(this);
        }

        public void Unregister()
        {
            SaveSlotManager.Instance?.Unregister(this);
        }

        public object ExportSaveData()
        {
            return new LootSaveData
            {
                PityCounters = new Dictionary<string, int>(_pityCounters)
            };
        }

        public void ImportSaveData(object data)
        {
            if (data is LootSaveData saveData && saveData.PityCounters != null)
            {
                _pityCounters.Clear();
                foreach (var kvp in saveData.PityCounters)
                {
                    _pityCounters[kvp.Key] = kvp.Value;
                }
            }
        }

        public void ResetToDefault()
        {
            _pityCounters.Clear();
        }

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
        }

        private void Start()
        {
            ResolveDefaultProviders();
            Register();
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        #endregion

        #region Public API

        public void SetInventoryProvider(IInventoryProvider provider)
        {
            _inventoryProvider = provider;
        }

        public void SetCurrencyProvider(ICurrencyProvider provider)
        {
            _currencyProvider = provider;
        }

        /// <summary>
        /// 执行掉落
        /// </summary>
        /// <param name="table">掉落表</param>
        /// <param name="times">抽取次数</param>
        /// <returns>掉落结果列表</returns>
        public List<LootResult> Roll(LootTableSO table, int times = 1)
        {
            return Roll(table, null, times);
        }

        /// <summary>
        /// 带上下文执行掉落
        /// </summary>
        public List<LootResult> Roll(LootTableSO table, LootContext context, int times = 1)
        {
            _tempResults.Clear();

            if (table == null)
            {
                LogWarning("掉落表为空");
                return new List<LootResult>(_tempResults);
            }

            context ??= new LootContext();
            context.InventoryProvider ??= GetInventoryProvider();
            context.CurrencyProvider ??= GetCurrencyProvider();

            // 检查全局条件
            if (!table.CheckGlobalConditions(context))
            {
                Log($"掉落表 {table.DisplayName} 全局条件不满足");
                return new List<LootResult>(_tempResults);
            }

            for (int t = 0; t < times; t++)
            {
                // 添加必掉物品
                AddGuaranteedDrops(table, context, _tempResults);

                // 根据模式执行掉落
                switch (table.DropMode)
                {
                    case LootDropMode.Weight:
                        RollWeight(table, context, _tempResults);
                        break;
                    case LootDropMode.Pity:
                        RollPity(table, context, _tempResults);
                        break;
                    case LootDropMode.Layered:
                        RollLayered(table, context, _tempResults);
                        break;
                }
            }

            // 触发事件
            var results = new List<LootResult>(_tempResults);
            OnLootEvent?.Invoke(LootEventArgs.Rolled(table, results.ToArray(), context));

            // 自动发放
            if (_autoGrantToInventory && results.Count > 0)
            {
                GrantResults(results);
            }

            Log($"掉落表 {table.DisplayName} 产出 {results.Count} 个结果");
            return results;
        }

        /// <summary>
        /// 执行掉落并直接发放到背包
        /// </summary>
        public void RollAndGrant(LootTableSO table, int times = 1)
        {
            RollAndGrant(table, null, times);
        }

        /// <summary>
        /// 带上下文执行掉落并发放
        /// </summary>
        public void RollAndGrant(LootTableSO table, LootContext context, int times = 1)
        {
            var results = Roll(table, context, times);
            if (!_autoGrantToInventory)
            {
                GrantResults(results);
            }
        }

        /// <summary>
        /// 发放掉落结果
        /// </summary>
        public void GrantResults(List<LootResult> results)
        {
            if (results == null || results.Count == 0) return;

            var inventory = GetInventoryProvider();
            var currency = GetCurrencyProvider();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];

                switch (result.Type)
                {
                    case LootEntryType.Item:
                        if (result.Item != null && inventory != null)
                        {
                            inventory.AddItem(result.Item, result.Amount);
                            Log($"发放物品: {result.Item.ItemName} x{result.Amount}");
                        }
                        break;

                    case LootEntryType.Currency:
                        currency?.AddCurrency(GetCurrencyId(result.Currency), result.Amount);
                        Log($"发放货币: {result.Currency} x{result.Amount}");
                        break;
                }
            }

            var args = LootEventArgs.Granted(null, results.ToArray());
            OnLootEvent?.Invoke(args);
            EventManager.Trigger(EconomyEvents.LootGranted, args);
        }

        /// <summary>
        /// 获取保底计数
        /// </summary>
        public int GetPityCount(LootTableSO table, int entryIndex)
        {
            string key = GetPityKey(table, entryIndex);
            return _pityCounters.TryGetValue(key, out var count) ? count : 0;
        }

        /// <summary>
        /// 重置保底计数
        /// </summary>
        public void ResetPity(LootTableSO table, int entryIndex)
        {
            string key = GetPityKey(table, entryIndex);
            _pityCounters.Remove(key);
        }

        /// <summary>
        /// 重置掉落表所有保底
        /// </summary>
        public void ResetAllPity(LootTableSO table)
        {
            if (table == null) return;

            string prefix = $"{table.TableId}_";
            var keysToRemove = new List<string>();

            foreach (var key in _pityCounters.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    keysToRemove.Add(key);
                }
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                _pityCounters.Remove(keysToRemove[i]);
            }
        }

        #endregion

        #region Roll Methods

        private void RollWeight(LootTableSO table, LootContext context, List<LootResult> results)
        {
            // 获取有效条目
            table.GetValidEntries(context, _tempValidEntries);
            if (_tempValidEntries.Count == 0) return;

            float totalWeight = table.CalculateTotalWeight(_tempValidEntries);
            if (totalWeight <= 0) return;

            _tempSelectedIndices.Clear();

            for (int d = 0; d < table.DropCount; d++)
            {
                // 权重随机
                float roll = UnityEngine.Random.Range(0f, totalWeight);
                float cumulative = 0;

                for (int i = 0; i < _tempValidEntries.Count; i++)
                {
                    // 不允许重复时跳过已选
                    if (!table.AllowDuplicates && _tempSelectedIndices.Contains(i))
                        continue;

                    cumulative += _tempValidEntries[i].Weight;

                    if (roll <= cumulative)
                    {
                        var entry = _tempValidEntries[i];
                        AddEntryToResults(entry, table, i, results, false);
                        _tempSelectedIndices.Add(i);
                        break;
                    }
                }

                // 检查最大掉落数
                if (table.MaxDropCount > 0 && results.Count >= table.MaxDropCount)
                    break;
            }
        }

        private void RollPity(LootTableSO table, LootContext context, List<LootResult> results)
        {
            table.GetValidEntries(context, _tempValidEntries);
            if (_tempValidEntries.Count == 0) return;

            for (int d = 0; d < table.DropCount; d++)
            {
                for (int i = 0; i < _tempValidEntries.Count; i++)
                {
                    var entry = _tempValidEntries[i];
                    if (entry.Pity == null) continue;

                    string pityKey = GetPityKey(table, i);
                    int pityCount = _pityCounters.TryGetValue(pityKey, out var c) ? c : 0;

                    // 计算当前概率（基础权重 + 累计增加）
                    float baseProb = entry.Weight / 100f; // 假设权重是百分比
                    float currentProb = baseProb + pityCount * entry.Pity.IncrementPerFail;
                    currentProb = Mathf.Clamp01(currentProb);

                    // 保底触发
                    bool isPity = pityCount >= entry.Pity.MaxAttempts - 1;
                    bool rolled = isPity || UnityEngine.Random.value <= currentProb;

                    if (rolled)
                    {
                        AddEntryToResults(entry, table, i, results, isPity);
                        _pityCounters[pityKey] = 0; // 重置计数

                        if (isPity)
                        {
                            OnLootEvent?.Invoke(LootEventArgs.PityTriggered(table, results[results.Count - 1]));
                            Log($"保底触发: {entry.GetDisplayName()}");
                        }

                        break; // 每次只掉一个
                    }
                    else
                    {
                        _pityCounters[pityKey] = pityCount + 1;
                    }
                }

                if (table.MaxDropCount > 0 && results.Count >= table.MaxDropCount)
                    break;
            }
        }

        private void RollLayered(LootTableSO table, LootContext context, List<LootResult> results)
        {
            if (table.Layers == null || table.Layers.Count == 0) return;

            // 计算层总权重
            float totalLayerWeight = 0;
            for (int i = 0; i < table.Layers.Count; i++)
            {
                totalLayerWeight += table.Layers[i].Weight;
            }

            if (totalLayerWeight <= 0) return;

            for (int d = 0; d < table.DropCount; d++)
            {
                // 先选层
                float layerRoll = UnityEngine.Random.Range(0f, totalLayerWeight);
                float cumulative = 0;
                LootLayer selectedLayer = null;

                for (int i = 0; i < table.Layers.Count; i++)
                {
                    cumulative += table.Layers[i].Weight;
                    if (layerRoll <= cumulative)
                    {
                        selectedLayer = table.Layers[i];
                        break;
                    }
                }

                if (selectedLayer == null || selectedLayer.Entries.Count == 0)
                    continue;

                // 在层内选条目
                _tempValidEntries.Clear();
                for (int i = 0; i < selectedLayer.Entries.Count; i++)
                {
                    var entry = selectedLayer.Entries[i];
                    if (entry.Weight > 0 && entry.CheckConditions(context))
                    {
                        _tempValidEntries.Add(entry);
                    }
                }

                if (_tempValidEntries.Count == 0) continue;

                float entryTotalWeight = 0;
                for (int i = 0; i < _tempValidEntries.Count; i++)
                {
                    entryTotalWeight += _tempValidEntries[i].Weight;
                }

                float entryRoll = UnityEngine.Random.Range(0f, entryTotalWeight);
                cumulative = 0;

                for (int i = 0; i < _tempValidEntries.Count; i++)
                {
                    cumulative += _tempValidEntries[i].Weight;
                    if (entryRoll <= cumulative)
                    {
                        AddEntryToResults(_tempValidEntries[i], table, i, results, false);
                        break;
                    }
                }

                if (table.MaxDropCount > 0 && results.Count >= table.MaxDropCount)
                    break;
            }
        }

        #endregion

        #region Helper Methods

        private void AddGuaranteedDrops(LootTableSO table, LootContext context, List<LootResult> results)
        {
            if (table.GuaranteedDrops == null) return;

            for (int i = 0; i < table.GuaranteedDrops.Count; i++)
            {
                var entry = table.GuaranteedDrops[i];
                if (entry.CheckConditions(context))
                {
                    AddEntryToResults(entry, table, -1, results, false);
                }
            }
        }

        private void AddEntryToResults(LootEntry entry, LootTableSO table, int index, List<LootResult> results, bool isPity)
        {
            switch (entry.Type)
            {
                case LootEntryType.Item:
                    if (entry.Item != null)
                    {
                        results.Add(new LootResult
                        {
                            Type = LootEntryType.Item,
                            Item = entry.Item,
                            Amount = entry.GetRandomAmount(),
                            WasPityDrop = isPity,
                            SourceTable = table,
                            SourceEntryIndex = index
                        });
                    }
                    break;

                case LootEntryType.Currency:
                    results.Add(new LootResult
                    {
                        Type = LootEntryType.Currency,
                        Currency = entry.Currency,
                        Amount = entry.GetRandomAmount(),
                        WasPityDrop = isPity,
                        SourceTable = table,
                        SourceEntryIndex = index
                    });
                    break;

                case LootEntryType.Table:
                    // 嵌套表递归
                    if (entry.NestedTable != null)
                    {
                        var nestedResults = Roll(entry.NestedTable, null, 1);
                        results.AddRange(nestedResults);
                    }
                    break;

                case LootEntryType.Nothing:
                    // 空掉落，不添加
                    break;
            }
        }

        private string GetPityKey(LootTableSO table, int entryIndex)
        {
            return $"{table.TableId}_{entryIndex}";
        }

        private void ResolveDefaultProviders()
        {
            _inventoryProvider ??= EconomyProviderResolver.FindInventoryProvider();
            _currencyProvider ??= EconomyProviderResolver.FindCurrencyProvider();
        }

        private IInventoryProvider GetInventoryProvider()
        {
            _inventoryProvider ??= EconomyProviderResolver.FindInventoryProvider();
            return _inventoryProvider;
        }

        private ICurrencyProvider GetCurrencyProvider()
        {
            _currencyProvider ??= EconomyProviderResolver.FindCurrencyProvider();
            return _currencyProvider;
        }

        private static string GetCurrencyId(CurrencyType currency)
        {
            return currency switch
            {
                CurrencyType.Gold => CurrencyIds.Gold,
                CurrencyType.Gem => CurrencyIds.Gem,
                CurrencyType.Token => CurrencyIds.Token,
                CurrencyType.Experience => CurrencyIds.Experience,
                _ => currency.ToString()
            };
        }

        #endregion

        #region Logging

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEROENGINE_DEBUG")]
        private void Log(string message)
        {
            if (_debugMode)
            {
                Debug.Log(string.Concat("[Loot] ", message));
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning(string.Concat("[Loot] ", message));
        }

        #endregion
    }

    #region Currency Event Data

    /// <summary>
    /// 货币事件数据
    /// </summary>
    public struct CurrencyEventData
    {
        public CurrencyType Type;
        public int Amount;

        public CurrencyEventData(CurrencyType type, int amount)
        {
            Type = type;
            Amount = amount;
        }
    }

    #endregion

    #region Save Data

    [Serializable]
    public class LootSaveData
    {
        public Dictionary<string, int> PityCounters = new Dictionary<string, int>();
    }

    #endregion
}
