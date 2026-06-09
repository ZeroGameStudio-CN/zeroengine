using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.TCE
{
    public sealed class TceRuntime
    {
        private readonly List<ITceTrigger> triggers = new();
        private readonly List<ITceCondition> conditions = new();
        private readonly List<ITceEffect> effects = new();
        private readonly List<ITceComponent> components = new();
        private readonly List<ITceExecutionAcceptedObserver> acceptedObservers = new();
        private readonly List<ITceExecutionObserver> executionObservers = new();

        public event Action<ITceActor, object> Executed;

        public void Install(object source, ITceActor owner, TceGraph graph, ITceClock clock = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            Uninstall();

            var context = new TceComponentContext(this, graph, owner, source, clock);
            CreateComponents(graph.Conditions, context, conditions);
            CreateComponents(graph.Effects, context, effects);
            CreateComponents(graph.Triggers.OrderByDescending(item => item.Order), context, triggers);

            foreach (ITceComponent component in components)
                component.OnBeforeInstall();

            foreach (ITceTrigger trigger in triggers)
                trigger.Triggered += HandleTriggered;

            foreach (ITceComponent component in components)
                component.OnInstall();
        }

        public void Uninstall()
        {
            foreach (ITceTrigger trigger in triggers)
                trigger.Triggered -= HandleTriggered;

            for (int i = components.Count - 1; i >= 0; i--)
                components[i].OnUninstall();

            triggers.Clear();
            conditions.Clear();
            effects.Clear();
            components.Clear();
            acceptedObservers.Clear();
            executionObservers.Clear();
        }

        private void HandleTriggered(ITceActor target, object source)
        {
            foreach (ITceCondition condition in conditions)
            {
                if (!condition.Check(target, source))
                    return;
            }

            foreach (ITceExecutionAcceptedObserver observer in acceptedObservers)
                observer.OnExecutionAccepted(target, source);

            foreach (ITceEffect effect in effects)
                effect.Execute(target, source);

            foreach (ITceExecutionObserver observer in executionObservers)
                observer.OnExecuted(target, source);

            Executed?.Invoke(target, source);
        }

        private void CreateComponents<TData, TComponent>(
            IEnumerable<TData> dataItems,
            TceComponentContext context,
            List<TComponent> destination)
            where TData : TceComponentData
            where TComponent : ITceComponent
        {
            foreach (TData data in dataItems)
            {
                if (data == null)
                    continue;

                if (!typeof(TComponent).IsAssignableFrom(data.RuntimeType))
                    throw new InvalidOperationException($"{data.RuntimeType.FullName} is not a {typeof(TComponent).Name}.");

                var component = (TComponent)Activator.CreateInstance(data.RuntimeType);
                component.Initialize(context, data);
                destination.Add(component);
                components.Add(component);

                if (component is ITceExecutionAcceptedObserver acceptedObserver)
                    acceptedObservers.Add(acceptedObserver);

                if (component is ITceExecutionObserver executionObserver)
                    executionObservers.Add(executionObserver);
            }
        }
    }
}
