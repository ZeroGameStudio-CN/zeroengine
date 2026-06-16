using UnityEngine;

namespace ZeroEngine.Currency
{
    [CreateAssetMenu(fileName = "CurrencyDefinition", menuName = "ZeroEngine/Economy/Currency Definition")]
    public class CurrencyDefinitionSO : ScriptableObject
    {
        [SerializeField] private string _currencyId;
        [SerializeField] private string _displayName;
        [SerializeField] private Sprite _icon;
        [SerializeField] private int _maxBalance = int.MaxValue;
        [SerializeField] private int _startingBalance;
        [SerializeField] private bool _allowNegative;

        public string CurrencyId => _currencyId;
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public int MaxBalance => _maxBalance <= 0 ? int.MaxValue : _maxBalance;
        public int StartingBalance => _startingBalance;
        public bool AllowNegative => _allowNegative;
    }
}
