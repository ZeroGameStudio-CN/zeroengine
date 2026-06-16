using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Economy;
using ZeroEngine.Save;

namespace ZeroEngine.Currency
{
    public class CurrencyManager : MonoSingleton<CurrencyManager>, ISaveable, ICurrencyProvider
    {
        [SerializeField] private List<CurrencyDefinitionSO> _definitions = new List<CurrencyDefinitionSO>();

        private readonly Dictionary<string, CurrencyDefinitionSO> _definitionMap =
            new Dictionary<string, CurrencyDefinitionSO>();
        private Dictionary<string, int> _balances = new Dictionary<string, int>();

        public event Action<CurrencyChangedEventArgs> OnCurrencyChanged;

        public string SaveKey => "CurrencyManager";

        protected override void Awake()
        {
            base.Awake();
            RebuildDefinitionCache();
            InitializeMissingBalances();
        }

        private void Start()
        {
            Register();
        }

        protected override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

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
            return new CurrencySaveData
            {
                Balances = new Dictionary<string, int>(_balances)
            };
        }

        public void ImportSaveData(object data)
        {
            _balances.Clear();
            if (data is CurrencySaveData saveData && saveData.Balances != null)
            {
                foreach (var kvp in saveData.Balances)
                {
                    _balances[kvp.Key] = ClampBalance(kvp.Key, kvp.Value);
                }
            }
            InitializeMissingBalances();
        }

        public void ResetToDefault()
        {
            _balances.Clear();
            InitializeMissingBalances();
        }

        public bool HasCurrency(string currencyId, int amount)
        {
            if (amount <= 0) return true;
            return GetCurrencyBalance(currencyId) >= amount;
        }

        public bool ConsumeCurrency(string currencyId, int amount)
        {
            if (string.IsNullOrEmpty(currencyId) || amount <= 0) return false;

            int current = GetCurrencyBalance(currencyId);
            bool allowNegative = GetAllowNegative(currencyId);
            if (current < amount && !allowNegative)
            {
                return false;
            }

            SetBalance(currencyId, current - amount, CurrencyEventType.Consumed);
            return true;
        }

        public void AddCurrency(string currencyId, int amount)
        {
            if (string.IsNullOrEmpty(currencyId) || amount <= 0) return;
            SetBalance(currencyId, GetCurrencyBalance(currencyId) + amount, CurrencyEventType.Added);
        }

        public int GetCurrencyBalance(string currencyId)
        {
            return !string.IsNullOrEmpty(currencyId) && _balances.TryGetValue(currencyId, out int balance)
                ? balance
                : 0;
        }

        public void SetBalance(string currencyId, int value, CurrencyEventType eventType = CurrencyEventType.Set)
        {
            if (string.IsNullOrEmpty(currencyId)) return;

            int previous = GetCurrencyBalance(currencyId);
            int clamped = ClampBalance(currencyId, value);
            if (previous == clamped && _balances.ContainsKey(currencyId))
            {
                return;
            }

            _balances[currencyId] = clamped;
            var args = new CurrencyChangedEventArgs
            {
                CurrencyId = currencyId,
                PreviousBalance = previous,
                NewBalance = clamped,
                Delta = clamped - previous,
                EventType = eventType
            };

            OnCurrencyChanged?.Invoke(args);
            EventManager.Trigger(EconomyEvents.CurrencyChanged, args);
        }

        public CurrencyDefinitionSO GetDefinition(string currencyId)
        {
            return !string.IsNullOrEmpty(currencyId) && _definitionMap.TryGetValue(currencyId, out var definition)
                ? definition
                : null;
        }

        public void RebuildDefinitionCache()
        {
            _definitionMap.Clear();
            for (int i = 0; i < _definitions.Count; i++)
            {
                var definition = _definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.CurrencyId))
                {
                    continue;
                }

                _definitionMap[definition.CurrencyId] = definition;
            }
        }

        private void InitializeMissingBalances()
        {
            for (int i = 0; i < _definitions.Count; i++)
            {
                var definition = _definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.CurrencyId))
                {
                    continue;
                }

                if (!_balances.ContainsKey(definition.CurrencyId))
                {
                    _balances[definition.CurrencyId] = ClampBalance(definition.CurrencyId, definition.StartingBalance);
                }
            }
        }

        private int ClampBalance(string currencyId, int value)
        {
            int max = int.MaxValue;
            bool allowNegative = false;
            if (_definitionMap.TryGetValue(currencyId, out var definition))
            {
                max = definition.MaxBalance;
                allowNegative = definition.AllowNegative;
            }

            int min = allowNegative ? int.MinValue : 0;
            return Mathf.Clamp(value, min, max);
        }

        private bool GetAllowNegative(string currencyId)
        {
            return _definitionMap.TryGetValue(currencyId, out var definition) && definition.AllowNegative;
        }
    }

    [Serializable]
    public class CurrencySaveData
    {
        public Dictionary<string, int> Balances = new Dictionary<string, int>();
    }
}
