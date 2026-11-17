using System;
using System.Threading;

namespace IndependentContent.Example
{
    // A separate content assembly. No ZE, Unity, networking or persistence dependency.
    public sealed class IndependentModule
    {
        private Action<bool> completed;
        private CancellationTokenRegistration cancellation;
        public bool IsRunning { get; private set; }
        public int StopCount { get; private set; }
        public void Begin(CancellationToken lifetime, Action<bool> onCompleted)
        {
            if (IsRunning) throw new InvalidOperationException("Stop the previous execution first.");
            completed = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted)); IsRunning = true;
            cancellation = lifetime.Register(Stop);
        }
        public Action<bool> CaptureCompletion() => completed;
        public void Finish(bool accepted = true) { if (IsRunning) completed?.Invoke(accepted); }
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false; StopCount++; completed = null; cancellation.Dispose();
        }
    }
}
