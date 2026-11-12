using System;
using UnityEngine;

namespace ZeroEngine.Tutorial.Guidance
{
    public sealed class TutorialSignalTrigger : MonoBehaviour
    {
        public TutorialManager Manager;
        public TutorialSequenceSO Sequence;
        public string EventKey;
        public string ExpectedValue;
        public bool StartOnEnable;
        [Min(.1f)] public float QueueLifetimeSeconds = 60;
        private IDisposable listener;
        private TutorialStartRequest pending;

        private void OnEnable()
        {
            if (!Manager) Manager = GetComponentInParent<TutorialManager>();
            if (!Manager) return; // Register explicitly; never create a manager just by enabling a trigger.
            listener = Manager.Context.Focus.Signals.Subscribe((key, value) =>
            {
                if (key == EventKey && (string.IsNullOrEmpty(ExpectedValue) || ExpectedValue == value)) Request();
            });
            if (StartOnEnable) Request();
        }
        public void Request()
        {
            if (!Manager || !Sequence || !isActiveAndEnabled || pending?.State == TutorialRequestState.Pending) return;
            var owner = Manager;
            pending = Manager.QueueSequence(Sequence, () => this && isActiveAndEnabled && Manager == owner, QueueLifetimeSeconds);
        }
        private void OnDisable() { listener?.Dispose(); listener = null; pending?.Dispose(); pending = null; }
        private void OnDestroy() { listener?.Dispose(); pending?.Dispose(); }
    }
}
