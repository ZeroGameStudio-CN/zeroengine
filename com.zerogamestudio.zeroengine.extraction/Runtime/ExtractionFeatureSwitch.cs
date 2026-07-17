using System;

namespace POB.Extraction
{
    public static class ExtractionFeatureSwitch
    {
        private static bool enabled;

        public static bool Enabled => enabled;

        // 供需要"关闭时不监听任何事件"的模块(如死亡/房间钩子)做响应式订阅/退订,
        // 而不是在启动时无条件订阅。仅在值真正变化时触发。
        public static event Action<bool> EnabledChanged;

        public static void SetEnabled(bool value)
        {
            if (enabled == value) return;
            enabled = value;
            EnabledChanged?.Invoke(value);
        }

        public static void SetEnabledForTests(bool value)
        {
            SetEnabled(value);
        }
    }
}
