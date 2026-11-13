using UnityEngine;
using ZeroEngine.Interaction.Core;

namespace ZeroEngine.Interaction.Unity
{
    public enum InteractionTimeSource { Scaled, Unscaled }

    /// <summary>Consumer owns input. Call Press/Release from its existing input or UI adapter.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionAgentBehaviour : MonoBehaviour, IInteractionRange
    {
        [SerializeField] private InteractionWorldBehaviour world;
        [SerializeField] private string actorId;
        [SerializeField, Min(0)] private float maxDistance = 2;
        [SerializeField] private bool distance2D = true;
        [SerializeField] private InteractionTimeSource timeSource;
        private IInteractionClock injectedClock;
        private string runtimeId;
        public InteractionAgent Runtime { get; private set; }
        public string ActorId => string.IsNullOrWhiteSpace(actorId) ? runtimeId ?? (runtimeId = "runtime-actor-" + System.Guid.NewGuid().ToString("N")) : actorId;

        private sealed class Clock : IInteractionClock
        {
            private readonly InteractionAgentBehaviour owner;
            internal Clock(InteractionAgentBehaviour owner) { this.owner = owner; }
            public double Now => owner.timeSource == InteractionTimeSource.Unscaled ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
        }

        public void Configure(InteractionWorldBehaviour scope, string id, float range = 2, IInteractionClock clock = null)
        {
            Unbind(); world = scope; actorId = id; maxDistance = range; injectedClock = clock;
            if (isActiveAndEnabled) Bind();
        }
        private void OnEnable()
        {
            if (!world) world = GetComponentInParent<InteractionWorldBehaviour>();
            Bind();
        }
        private void OnDisable() { Unbind(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
        private void Bind()
        {
            if (!world) return;
            world.RuntimeChanged += Rebind; Rebind();
        }
        private void Unbind()
        {
            if (world) world.RuntimeChanged -= Rebind;
            Runtime?.Dispose(); Runtime = null;
        }
        private void Rebind()
        {
            Runtime?.Dispose(); Runtime = null;
            if (isActiveAndEnabled && world && world.Runtime != null)
                Runtime = new InteractionAgent(world.Runtime, ActorId, gameObject, injectedClock ?? new Clock(this), this, maxDistance);
        }
        private void Update() { Runtime?.Tick(); }
        public bool Press() => Runtime != null && Runtime.Press();
        public void Release() { Runtime?.Release(); }
        public void Cancel() { Runtime?.Cancel(); }

        public bool TryGetSquaredDistance(IInteractionTarget target, out double distance)
        {
            distance = 0;
            if (!(target is InteractionTargetBehaviour component) || !component) return false;
            Vector3 delta = component.transform.position - transform.position;
            if (distance2D) delta.z = 0;
            distance = delta.sqrMagnitude;
            return true;
        }
    }
}
