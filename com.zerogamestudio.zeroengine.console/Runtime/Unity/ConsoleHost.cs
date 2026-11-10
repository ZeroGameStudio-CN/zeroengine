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
        [SerializeField] private string[] capabilities = Array.Empty<string>();
        private UnityConsoleDispatcher dispatcher;
        private IDisposable common;
        public ConsoleRegistry Registry { get; private set; }
        public ConsoleSession Session { get; private set; }
        public Func<ConsoleCaller> CallerFactory { get; set; }
        public event Action<ConsoleHost> Started;
        public event Action<ConsoleHost> Stopping;

        private void OnEnable() => Initialize();

        public void Initialize()
        {
            if (Session != null || !isActiveAndEnabled) return;
            if (!Application.isEditor && !Debug.isDebugBuild && !allowInRelease) return;
            dispatcher = new UnityConsoleDispatcher();
            Registry = new ConsoleRegistry();
            Session = new ConsoleSession(Registry, dispatcher: dispatcher);
            common = ConsoleCommonCommands.Register(Session);
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

        private void OnDisable()
        {
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
