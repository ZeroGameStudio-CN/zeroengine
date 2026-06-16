using UnityEngine;
using ZeroEngine.TCE;

namespace ZeroEngine.TCE.Presentation
{
    public sealed class TcePresentationPlayer : ITcePresentationPlayer
    {
        private const string RunnerName = "ZeroEngine TCE Presentation Runner";
        private readonly TcePresentationRunner runner;

        public TcePresentationPlayer(TcePresentationRunner runner = null)
        {
            this.runner = runner ? runner : GetOrCreateRunner();
        }

        public TcePresentationHandle Play(TceVisualSnapshot snapshot, TcePresentationPlaybackSettings settings, ITceClock clock)
        {
            return runner.Play(snapshot, settings, clock);
        }

        private static TcePresentationRunner GetOrCreateRunner()
        {
            var existing = Object.FindFirstObjectByType<TcePresentationRunner>();
            if (existing)
                return existing;

            var host = new GameObject(RunnerName);
            Object.DontDestroyOnLoad(host);
            return host.AddComponent<TcePresentationRunner>();
        }
    }
}
