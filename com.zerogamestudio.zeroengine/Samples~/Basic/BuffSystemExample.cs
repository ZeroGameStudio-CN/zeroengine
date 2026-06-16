using UnityEngine;
using ZeroEngine.BuffSystem;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Samples
{
    /// <summary>
    /// BuffSystem 基础示例
    /// 演示 Buff 添加、移除、事件监听和属性修饰
    /// </summary>
    public class BuffSystemExample : MonoBehaviour
    {
        private static readonly StatId Attack = "offense.attack";

        [Header("Buff Data")]
        [SerializeField] private BuffData testBuff;

        private BuffReceiver _buffReceiver;
        private StatController _statController;

        private void Start()
        {
            _buffReceiver = GetComponent<BuffReceiver>();
            _statController = GetComponent<StatController>();

            if (_buffReceiver == null)
            {
                Debug.LogError("[BuffExample] BuffReceiver component required!");
                return;
            }

            // 监听 Buff 事件
            _buffReceiver.OnBuffApplied += OnBuffApplied;
            _buffReceiver.OnBuffRemoved += OnBuffRemoved;
            _buffReceiver.OnBuffChanged += OnBuffChanged;

            // 测试添加 Buff
            if (testBuff != null)
            {
                TestBuffOperations();
            }
            else
            {
                Debug.Log("[BuffExample] No test buff assigned. Use BuffUtils to create runtime buffs.");
                TestRuntimeBuff();
            }
        }

        private void TestBuffOperations()
        {
            Debug.Log("[BuffExample] Adding buff...");
            var handler = _buffReceiver.AddBuff(testBuff);

            Debug.Log($"[BuffExample] Buff stacks: {handler.CurrentStacks}");
            Debug.Log($"[BuffExample] Remaining time: {handler.RemainingTime}s");

            // 添加更多层数
            _buffReceiver.AddBuff(testBuff, 2);
            Debug.Log($"[BuffExample] After adding 2 stacks: {_buffReceiver.GetBuffStacks(testBuff.BuffId)}");
        }

        private void TestRuntimeBuff()
        {
            // 使用 BuffUtils 创建运行时 Buff
            var tempBuff = BuffUtils.CreateStatBuff(
                "temp_attack_boost",
                Attack,
                10f,
                StatModType.Flat,
                duration: 5f
            );

            Debug.Log("[BuffExample] Created runtime buff: temp_attack_boost");
            _buffReceiver.AddBuff(tempBuff);
        }

        private void OnBuffApplied(BuffHandler handler)
        {
            Debug.Log($"[BuffExample] Buff applied: {handler.Data.BuffId}");
        }

        private void OnBuffRemoved(BuffHandler handler, BuffEventType reason)
        {
            Debug.Log($"[BuffExample] Buff removed: {handler.Data.BuffId}, reason: {reason}");
        }

        private void OnBuffChanged(BuffEventArgs args)
        {
            Debug.Log($"[BuffExample] Buff changed: {args.Buff.Data.BuffId}, " +
                      $"type: {args.EventType}, stacks: {args.OldStacks} -> {args.NewStacks}");
        }

        private void Update()
        {
            // 按 R 键移除所有 Buff
            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("[BuffExample] Removing all buffs...");
                _buffReceiver.RemoveAllBuffs();
            }
        }

        private void OnDestroy()
        {
            if (_buffReceiver != null)
            {
                _buffReceiver.OnBuffApplied -= OnBuffApplied;
                _buffReceiver.OnBuffRemoved -= OnBuffRemoved;
                _buffReceiver.OnBuffChanged -= OnBuffChanged;
            }
        }
    }
}
