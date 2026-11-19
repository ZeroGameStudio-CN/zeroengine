using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;

namespace ZeroEngine.TCE
{
    public sealed class TceRuntime
    {
        private sealed class Installation
        {
            public readonly List<ITceTrigger> Triggers = new();
            public readonly List<ITceCondition> Conditions = new();
            public readonly List<ITceEffect> Effects = new();
            public readonly List<ITceComponent> Components = new();
            public readonly List<ITceExecutionAcceptedObserver> AcceptedObservers = new();
            public readonly List<ITceExecutionObserver> ExecutionObservers = new();
            public Action<ITceActor, object> TriggerHandler;
            public bool Released;
        }

        private Installation current;

        public event Action<ITceActor, object> Executed;

        public void Install(object source, ITceActor owner, TceGraph graph, ITceClock clock = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            var installation = new Installation();
            var previous = current;
            // Reserve this request before callbacks: a newer Install/Uninstall wins.
            current = installation;
            try
            {
                Release(previous);
                if (!IsCurrent(installation)) return;

                var context = new TceComponentContext(this, graph, owner, source, clock);
                if (!CreateComponents(graph.Conditions, context, installation, installation.Conditions)
                    || !CreateComponents(graph.Effects, context, installation, installation.Effects)
                    || !CreateComponents(graph.Triggers.OrderByDescending(item => item.Order), context,
                        installation, installation.Triggers)) return;

                foreach (ITceComponent component in installation.Components)
                {
                    component.OnBeforeInstall();
                    if (!IsCurrent(installation)) return;
                }

                installation.TriggerHandler = (target, triggerSource) => HandleTriggered(installation, target, triggerSource);
                foreach (ITceTrigger trigger in installation.Triggers)
                {
                    trigger.Triggered += installation.TriggerHandler;
                    if (!IsCurrent(installation)) return;
                }

                foreach (ITceComponent component in installation.Components)
                {
                    component.OnInstall();
                    if (!IsCurrent(installation)) return;
                }
            }
            catch (Exception error)
            {
                if (ReferenceEquals(current, installation)) current = null;
                try { Release(installation); }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("TCE installation and its cleanup failed.", error, cleanupError);
                }
                throw;
            }
        }

        public void Uninstall()
        {
            var installation = current;
            current = null;
            Release(installation);
        }

        private bool IsCurrent(Installation installation) => ReferenceEquals(current, installation) && !installation.Released;

        private static void Release(Installation installation)
        {
            if (installation == null || installation.Released) return;
            installation.Released = true;
            List<Exception> errors = null;
            foreach (ITceTrigger trigger in installation.Triggers)
            {
                try { trigger.Triggered -= installation.TriggerHandler; }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            }
            for (int i = installation.Components.Count - 1; i >= 0; i--)
            {
                try { installation.Components[i].OnUninstall(); }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            }
            installation.Triggers.Clear();
            installation.Conditions.Clear();
            installation.Effects.Clear();
            installation.Components.Clear();
            installation.AcceptedObservers.Clear();
            installation.ExecutionObservers.Clear();
            installation.TriggerHandler = null;
            if (errors?.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors?.Count > 1) throw new AggregateException("TCE component cleanup failed.", errors);
        }

        private void HandleTriggered(Installation installation, ITceActor target, object source)
        {
            if (!IsCurrent(installation)) return;
            foreach (ITceCondition condition in installation.Conditions)
            {
                bool accepted = condition.Check(target, source);
                if (!IsCurrent(installation) || !accepted) return;
            }

            foreach (ITceExecutionAcceptedObserver observer in installation.AcceptedObservers)
            {
                observer.OnExecutionAccepted(target, source);
                if (!IsCurrent(installation)) return;
            }

            foreach (ITceEffect effect in installation.Effects)
            {
                effect.Execute(target, source);
                if (!IsCurrent(installation)) return;
            }

            foreach (ITceExecutionObserver observer in installation.ExecutionObservers)
            {
                observer.OnExecuted(target, source);
                if (!IsCurrent(installation)) return;
            }

            Executed?.Invoke(target, source);
        }

        private bool CreateComponents<TData, TComponent>(
            IEnumerable<TData> dataItems,
            TceComponentContext context,
            Installation installation,
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
                installation.Components.Add(component);
                component.Initialize(context, data);
                if (!IsCurrent(installation)) return false;
                destination.Add(component);

                if (component is ITceExecutionAcceptedObserver acceptedObserver)
                    installation.AcceptedObservers.Add(acceptedObserver);

                if (component is ITceExecutionObserver executionObserver)
                    installation.ExecutionObservers.Add(executionObserver);
            }
            return true;
        }
    }
}
