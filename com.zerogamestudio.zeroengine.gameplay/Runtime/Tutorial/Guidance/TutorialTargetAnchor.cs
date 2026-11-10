using System;
using UnityEngine;

namespace ZeroEngine.Tutorial.Guidance
{
    [DisallowMultipleComponent]
    public sealed class TutorialTargetAnchor : MonoBehaviour
    {
        public string TargetId;
        public TutorialManager Manager;
        private TutorialContext boundContext;
        private IDisposable registration;
        public void Bind(TutorialContext context)
        {
            registration?.Dispose(); registration = null; boundContext = context;
            if (isActiveAndEnabled) Register();
        }
        private void OnEnable()
        {
            if (boundContext == null)
            {
                var manager = Manager ? Manager : GetComponentInParent<TutorialManager>();
                if (manager) boundContext = manager.Context;
            }
            Register();
        }
        private void Register()
        {
            if (registration == null && boundContext != null && !string.IsNullOrWhiteSpace(TargetId))
                registration = boundContext.Focus.Targets.Register(TargetId, gameObject);
        }
        private void OnDisable() { registration?.Dispose(); registration = null; }
        private void OnDestroy() { registration?.Dispose(); registration = null; }
    }
}
