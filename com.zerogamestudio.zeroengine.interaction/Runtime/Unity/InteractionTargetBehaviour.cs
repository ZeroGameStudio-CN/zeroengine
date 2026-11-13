using System;
using UnityEngine;
using ZeroEngine.Interaction.Core;

namespace ZeroEngine.Interaction.Unity
{
    /// <summary>Spatial/activation bridge only. Consumers supply conditions and an action explicitly.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionTargetBehaviour : MonoBehaviour, IInteractionTarget
    {
        [SerializeField] private InteractionWorldBehaviour world;
        [SerializeField] private string targetId;
        [SerializeField] private int priority;
        [SerializeField] private InteractionMode mode;
        [SerializeField, Min(.01f)] private float holdSeconds = .5f;
        [SerializeField, Min(0)] private float repeatSeconds;
        [SerializeField] private bool available = true;
        private InteractionRegistration registration;
        private string runtimeId;
        public string Id => string.IsNullOrWhiteSpace(targetId) ? runtimeId ?? (runtimeId = "runtime-target-" + Guid.NewGuid().ToString("N")) : targetId;
        public int Priority => priority;
        public bool IsAvailable => this && isActiveAndEnabled && available;
        public InteractionPolicy Policy => new InteractionPolicy(mode, holdSeconds, repeatSeconds);
        public InteractionRegistration Registration => registration;
        public event Func<InteractionContext, InteractionCheck> Checking;
        public event Action<InteractionRequest> Requested;

        public void Configure(InteractionWorldBehaviour scope, string id, InteractionPolicy policy, int targetPriority = 0)
        {
            Unbind(); world = scope; targetId = id; priority = targetPriority;
            mode = policy.Mode; holdSeconds = (float)policy.HoldSeconds; repeatSeconds = (float)policy.RepeatSeconds;
            if (isActiveAndEnabled) Bind();
        }

        private void OnEnable()
        {
            if (!world) world = GetComponentInParent<InteractionWorldBehaviour>();
            Bind();
        }
        private void OnDisable() { Unbind(); }
        private void Bind()
        {
            if (!world) return;
            world.RuntimeChanged += Rebind;
            Rebind();
        }
        private void Unbind()
        {
            if (world) world.RuntimeChanged -= Rebind;
            registration?.Dispose(); registration = null;
        }
        private void Rebind()
        {
            registration?.Dispose(); registration = null;
            if (isActiveAndEnabled && world && world.Runtime != null) registration = world.Runtime.Register(this);
        }

        public void SetAvailable(bool value) { available = value; }

        public InteractionCheck Check(InteractionContext context)
        {
            if (!IsAvailable) return InteractionCheck.Deny("Target is unavailable.");
            if (Requested == null) return InteractionCheck.Deny("No interaction action is configured.");
            if (Checking != null)
            {
                foreach (Func<InteractionContext, InteractionCheck> check in Checking.GetInvocationList())
                {
                    var result = check(context);
                    if (!result.Allowed) return result;
                }
            }
            return InteractionCheck.Permit;
        }

        public void Execute(InteractionRequest request)
        {
            var action = Requested;
            if (action == null) request.TryDeny("No interaction action is configured.");
            else action(request);
        }
    }
}
