using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.Console.Unity
{
    [DisallowMultipleComponent]
    public sealed class ConsoleHost : MonoBehaviour
    {
        [SerializeField] private bool allowInRelease;
        [SerializeField] private bool captureUnityLogs;
        [SerializeField] private string[] capabilities = Array.Empty<string>();
        private UnityConsoleDispatcher dispatcher;
        private IDisposable common;
        private bool capturingUnityLogs;
        public ConsoleRegistry Registry { get; private set; }
        public ConsoleSession Session { get; private set; }
        public Func<ConsoleCaller> CallerFactory { get; set; }
        public event Action<ConsoleHost> Started;
        public event Action<ConsoleHost> Stopping;
        public bool CaptureUnityLogs
        {
            get => captureUnityLogs;
            set { captureUnityLogs = value; UpdateLogCapture(); }
        }

        private void OnEnable() => Initialize();

        public void Initialize()
        {
            if (Session != null || !isActiveAndEnabled) return;
            if (!Application.isEditor && !Debug.isDebugBuild && !allowInRelease) return;
            dispatcher = new UnityConsoleDispatcher();
            Registry = new ConsoleRegistry();
            Session = new ConsoleSession(Registry, dispatcher: dispatcher);
            common = ConsoleCommonCommands.Register(Session);
            UpdateLogCapture();
            Started?.Invoke(this);
        }

        public ConsoleCaller CreateCaller()
        {
            if (CallerFactory != null) return CallerFactory();
            var environment = Application.isEditor ? ConsoleEnvironment.Editor : Debug.isDebugBuild ? ConsoleEnvironment.DevelopmentPlayer : ConsoleEnvironment.Player;
            return new ConsoleCaller(environment, capabilities: capabilities);
        }

        public Task<ConsoleResult> ExecuteAsync(string line, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            var session = Session;
            var target = dispatcher;
            return session == null || target == null ? Task.FromResult(ConsoleResult.Failure("host-disabled", "Unity Console host is disabled."))
                : target.DispatchAsync(() => session.ExecuteLineAsync(line, CreateCaller(), cancellation, timeout), cancellation);
        }

        public Task<ConsoleResult> ExecuteAsync(string idOrName, object[] arguments, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            var session = Session;
            var target = dispatcher;
            return session == null || target == null ? Task.FromResult(ConsoleResult.Failure("host-disabled", "Unity Console host is disabled."))
                : target.DispatchAsync(() => session.ExecuteAsync(idOrName, arguments, CreateCaller(), cancellation, timeout), cancellation);
        }

        private void UpdateLogCapture()
        {
            bool wanted = captureUnityLogs && Session != null;
            if (wanted == capturingUnityLogs) return;
            if (wanted) Application.logMessageReceivedThreaded += ReceiveUnityLog;
            else Application.logMessageReceivedThreaded -= ReceiveUnityLog;
            capturingUnityLogs = wanted;
        }

        private void ReceiveUnityLog(string message, string stackTrace, LogType type)
        {
            var session = Session;
            var level = type == LogType.Warning ? ConsoleOutputLevel.Warning :
                type == LogType.Error || type == LogType.Assert || type == LogType.Exception ? ConsoleOutputLevel.Error : ConsoleOutputLevel.Info;
            session?.Write(new ConsoleOutput("unity", level, message));
        }

        private void OnDisable()
        {
            if (capturingUnityLogs)
            {
                Application.logMessageReceivedThreaded -= ReceiveUnityLog;
                capturingUnityLogs = false;
            }
            if (Session == null) return;
            try { Stopping?.Invoke(this); }
            finally
            {
                try { Session.Dispose(); }
                finally
                {
                    try { common?.Dispose(); Registry.Dispose(); }
                    finally
                    {
                        dispatcher.Dispose();
                        common = null; Session = null; Registry = null; dispatcher = null;
                    }
                }
            }
        }
    }
}
