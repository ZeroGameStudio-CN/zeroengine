using UnityEngine;
using ZeroEngine.TCE;

public sealed class MinimalTceGraphExample : MonoBehaviour
{
    [SerializeField] private string message = "Minimal TCE graph executed.";
    [SerializeField] private float sourceValue = 1f;
    [SerializeField] private float requiredValue = 1f;

    private readonly TceRuntime runtime = new();
    private SampleActor actor;

    private void OnEnable()
    {
        actor = new SampleActor(this);

        var graph = new TceGraph();
        graph.AddTrigger(new OnInstallTriggerData());
        graph.AddCondition(new NumericSourceConditionData
        {
            RequiredValue = requiredValue,
            Comparison = TceComparison.GreaterThanOrEqualTo
        });
        graph.AddCondition(new CooldownConditionData { Duration = 0f });
        graph.AddEffect(new DebugLogEffectData { Message = message });

        runtime.Install(new NumericValueSource(sourceValue), actor, graph);
    }

    private void OnDisable()
    {
        runtime.Uninstall();
    }

    private sealed class SampleActor : ITceActor
    {
        private readonly MonoBehaviour owner;

        public SampleActor(MonoBehaviour owner)
        {
            this.owner = owner;
        }

        public bool IsAlive => owner != null && owner.isActiveAndEnabled;
        public float DomainTime => Time.time;
        public object NativeObject => owner;
    }
}
