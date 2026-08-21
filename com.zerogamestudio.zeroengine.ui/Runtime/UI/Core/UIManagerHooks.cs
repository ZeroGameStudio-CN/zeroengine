using System;
using System.Threading.Tasks;

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
    /// 可选的 Prefab 加载集成边界。宿主可在实际加载前准备资源系统，并记录加载结果。
    /// </summary>
    public interface IUIManagerPrefabLoadHooks
    {
        Task PreparePrefabLoadAsync(string resourceKey);
        void RecordPrefabLoad(string resourceKey, TimeSpan duration, bool succeeded);
    }

    /// <summary>
    /// 可选的委托式 hook，便于小型宿主或测试注入。
    /// </summary>
    public sealed class UIManagerHooks : IUIManagerHooks, IUIManagerPrefabLoadHooks
    {
        private readonly Action<bool> _pause;
        private readonly Action<UIManagerLogLevel, string> _log;
        private readonly Func<string, Task> _preparePrefabLoad;
        private readonly Action<string, TimeSpan, bool> _recordPrefabLoad;

        public UIManagerHooks(
            Action<bool> pause = null,
            Action<UIManagerLogLevel, string> log = null,
            Func<string, Task> preparePrefabLoad = null,
            Action<string, TimeSpan, bool> recordPrefabLoad = null)
        {
            _pause = pause;
            _log = log;
            _preparePrefabLoad = preparePrefabLoad;
            _recordPrefabLoad = recordPrefabLoad;
        }

        public void RequestPause(bool pause)
        {
            _pause?.Invoke(pause);
        }

        public void Log(UIManagerLogLevel level, string message)
        {
            _log?.Invoke(level, message);
        }

        public Task PreparePrefabLoadAsync(string resourceKey)
        {
            return _preparePrefabLoad?.Invoke(resourceKey) ?? Task.CompletedTask;
        }

        public void RecordPrefabLoad(string resourceKey, TimeSpan duration, bool succeeded)
        {
            _recordPrefabLoad?.Invoke(resourceKey, duration, succeeded);
        }
    }
}
