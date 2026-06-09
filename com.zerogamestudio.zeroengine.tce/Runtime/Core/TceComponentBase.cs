using System;

namespace ZeroEngine.TCE
{
    public sealed class TceComponentContext
    {
        public TceComponentContext(TceRuntime runtime, TceGraph graph, ITceActor owner, object installSource, ITceClock clock)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InstallSource = installSource;
            Clock = clock ?? new TceActorClock(owner);
        }

        public TceRuntime Runtime { get; }
        public TceGraph Graph { get; }
        public ITceActor Owner { get; }
        public object InstallSource { get; }
        public ITceClock Clock { get; }
    }

    public interface ITceComponent
    {
        void Initialize(TceComponentContext context, TceComponentData data);
        void OnBeforeInstall();
        void OnInstall();
        void OnUninstall();
    }

    public interface ITceTrigger : ITceComponent
    {
        event Action<ITceActor, object> Triggered;
    }

    public interface ITceCondition : ITceComponent
    {
        bool Check(ITceActor target, object source);
    }

    public interface ITceEffect : ITceComponent
    {
        void Execute(ITceActor target, object source);
    }

    public interface ITceExecutionAcceptedObserver : ITceComponent
    {
        void OnExecutionAccepted(ITceActor target, object source);
    }

    public interface ITceExecutionObserver : ITceComponent
    {
        void OnExecuted(ITceActor target, object source);
    }

    public abstract class TceComponent<TData> : ITceComponent where TData : TceComponentData
    {
        protected TceComponentContext Context { get; private set; }
        protected TData Data { get; private set; }

        public void Initialize(TceComponentContext context, TceComponentData data)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Data = data as TData ?? throw new ArgumentException($"Expected {typeof(TData).Name}, got {data?.GetType().Name ?? "null"}.");
        }

        public virtual void OnBeforeInstall()
        {
        }

        public virtual void OnInstall()
        {
        }

        public virtual void OnUninstall()
        {
        }
    }

    public abstract class TceTrigger<TData> : TceComponent<TData>, ITceTrigger where TData : TceTriggerData
    {
        public event Action<ITceActor, object> Triggered;

        protected void Trigger(ITceActor target, object source = null)
        {
            if (target == null || !target.IsAlive)
                return;

            Triggered?.Invoke(target, source);
        }
    }

    public abstract class TceCondition<TData> : TceComponent<TData>, ITceCondition where TData : TceConditionData
    {
        public virtual bool Check(ITceActor target, object source)
        {
            return true;
        }
    }

    public abstract class TceEffect<TData> : TceComponent<TData>, ITceEffect where TData : TceEffectData
    {
        public virtual void Execute(ITceActor target, object source)
        {
        }

        protected ITceActor ResolveTarget(ITceActor triggerTarget, object source)
        {
            return Data.Target switch
            {
                TceTargetMode.FromTrigger => triggerTarget,
                TceTargetMode.Self => Context.Owner,
                TceTargetMode.Source => source as ITceActor ?? triggerTarget,
                _ => triggerTarget
            };
        }
    }
}
