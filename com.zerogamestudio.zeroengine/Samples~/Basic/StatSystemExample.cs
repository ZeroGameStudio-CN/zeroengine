using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Samples
{
    /// <summary>
    /// StatSystem 基础示例
    /// 演示属性创建、修饰器添加和事件监听
    /// </summary>
    public class StatSystemExample : MonoBehaviour
    {
        private static readonly StatId Health = "core.health";
        private static readonly StatId Attack = "offense.attack";

        [Header("Stats")]
        [SerializeField] private float baseHealth = 100f;
        [SerializeField] private float baseAttack = 10f;

        private StatController _statController;

        private void Start()
        {
            _statController = GetComponent<StatController>();
            if (_statController == null)
            {
                _statController = gameObject.AddComponent<StatController>();
            }

            // 初始化属性
            _statController.InitStat(Health, baseHealth, 0, 9999);
            _statController.InitStat(Attack, baseAttack);

            // 监听属性变化
            var healthStat = _statController.GetStat(Health);
            healthStat.OnValueChanged += OnHealthChanged;

            Debug.Log($"[StatExample] Health: {_statController.GetStatValue(Health)}");
            Debug.Log($"[StatExample] Attack: {_statController.GetStatValue(Attack)}");

            // 测试修饰器
            TestModifiers();
        }

        private void TestModifiers()
        {
            // 添加固定值修饰器 (+50 生命)
            var flatMod = new StatModifier(50f, StatModType.Flat);
            _statController.AddModifier(Health, flatMod);
            Debug.Log($"[StatExample] After +50 Flat: Health = {_statController.GetStatValue(Health)}");

            // 添加百分比修饰器 (+20% 生命)
            var percentMod = new StatModifier(0.2f, StatModType.PercentAdd);
            _statController.AddModifier(Health, percentMod);
            Debug.Log($"[StatExample] After +20% PercentAdd: Health = {_statController.GetStatValue(Health)}");

            // 移除修饰器
            _statController.RemoveModifier(Health, flatMod);
            Debug.Log($"[StatExample] After removing Flat: Health = {_statController.GetStatValue(Health)}");
        }

        private void OnHealthChanged(StatChangedEventArgs args)
        {
            Debug.Log($"[StatExample] Health changed: {args.OldValue} -> {args.NewValue} (Delta: {args.Delta})");
        }

        private void OnDestroy()
        {
            var healthStat = _statController?.GetStat(Health);
            if (healthStat != null)
            {
                healthStat.OnValueChanged -= OnHealthChanged;
            }
        }
    }
}
