using System;

namespace ZeroEngine.UI
{
    /// <summary>
    /// UIManager 与项目运行时之间的最小集成边界。
    /// UI 包不依赖具体的暂停服务或日志实现；宿主可注入自己的实现。
    /// </summary>
    public interface IUIManagerHooks
    {
        void RequestPause(bool pause);
        void Log(UIManagerLogLevel level, string message);
    }

    /// <summary>
    /// 可选的委托式 hook，便于小型宿主或测试注入。
    /// </summary>
    public sealed class UIManagerHooks : IUIManagerHooks
    {
        private readonly Action<bool> _pause;
        private readonly Action<UIManagerLogLevel, string> _log;

        public UIManagerHooks(
            Action<bool> pause = null,
            Action<UIManagerLogLevel, string> log = null)
        {
            _pause = pause;
            _log = log;
        }

        public void RequestPause(bool pause)
        {
            _pause?.Invoke(pause);
        }

        public void Log(UIManagerLogLevel level, string message)
        {
            _log?.Invoke(level, message);
        }
    }
}
