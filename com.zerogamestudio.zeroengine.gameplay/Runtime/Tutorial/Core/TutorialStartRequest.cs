using System;

namespace ZeroEngine.Tutorial
{
    public enum TutorialRequestState { Pending = 0, Started = 1, Cancelled = 2, Rejected = 3, Expired = 4 }

    public sealed class TutorialStartRequest : IDisposable
    {
        public TutorialRequestState State { get; internal set; }
        internal TutorialSequenceSO Sequence;
        internal Func<bool> IsValid;
        internal double Deadline;
        internal long Order;
        public void Dispose() { if (State == TutorialRequestState.Pending) State = TutorialRequestState.Cancelled; }
    }
}
