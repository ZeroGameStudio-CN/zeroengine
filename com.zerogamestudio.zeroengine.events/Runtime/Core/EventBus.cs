using System;
using System.Collections.Generic;
using System.Threading;

namespace ZeroEngine.Events
{
    public sealed class EventBus : IEventBus
    {
        private sealed class Subscription
        {
            public Guid Id;
            public Type EventType;
            public Delegate Handler;
            public object Owner;
            public int Priority;
            public long Order;
        }

        private readonly Dictionary<Type, List<Subscription>> _subscriptions = new();
        private readonly EventBusDiagnostics _diagnostics;
        private readonly EventBusOptions _options;
        private long _nextOrder;
        private long _nextSequence;

        public EventBus(EventBusDiagnostics diagnostics = null, EventBusOptions? options = null)
        {
            _diagnostics = diagnostics ?? new EventBusDiagnostics();
            _options = options ?? EventBusOptions.CaptureCurrentThread();
        }

        public EventBusDiagnostics Diagnostics => _diagnostics;

        public EventSubscriptionToken Subscribe<TEvent>(
            Action<EventEnvelope<TEvent>> handler,
            EventSubscriptionOptions options = default)
            where TEvent : IGameEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var eventType = typeof(TEvent);
            if (!_subscriptions.TryGetValue(eventType, out var list))
            {
                list = new List<Subscription>();
                _subscriptions[eventType] = list;
            }

            var subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                EventType = eventType,
                Handler = handler,
                Owner = options.Owner,
                Priority = options.Priority,
                Order = _nextOrder++
            };

            list.Add(subscription);
            list.Sort(CompareSubscriptions);

            return new EventSubscriptionToken(subscription.Id, eventType, subscription.Owner, token => Unsubscribe(token));
        }

        public bool Unsubscribe(EventSubscriptionToken token)
        {
            if (!token.IsValid)
            {
                return false;
            }

            if (!_subscriptions.TryGetValue(token.EventType, out var list))
            {
                return false;
            }

            var removed = list.RemoveAll(item => item.Id == token.Id) > 0;
            if (list.Count == 0)
            {
                _subscriptions.Remove(token.EventType);
            }

            return removed;
        }

        public int UnsubscribeOwner(object owner)
        {
            if (owner == null)
            {
                return 0;
            }

            var removed = 0;
            var emptyTypes = new List<Type>();

            foreach (var pair in _subscriptions)
            {
                removed += pair.Value.RemoveAll(item => ReferenceEquals(item.Owner, owner));
                if (pair.Value.Count == 0)
                {
                    emptyTypes.Add(pair.Key);
                }
            }

            foreach (var type in emptyTypes)
            {
                _subscriptions.Remove(type);
            }

            return removed;
        }

        public int CountSubscriptions<TEvent>()
            where TEvent : IGameEvent
        {
            return _subscriptions.TryGetValue(typeof(TEvent), out var list) ? list.Count : 0;
        }

        public int CountSubscriptionsForOwner(object owner)
        {
            if (owner == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var pair in _subscriptions)
            {
                count += pair.Value.FindAll(item => ReferenceEquals(item.Owner, owner)).Count;
            }

            return count;
        }

        public int CountAllSubscriptions()
        {
            var count = 0;
            foreach (var pair in _subscriptions)
            {
                count += pair.Value.Count;
            }

            return count;
        }

        public int CopySubscriptionRecords(EventSubscriptionDiagnosticRecord[] destination)
        {
            if (destination == null || destination.Length == 0)
            {
                return 0;
            }

            var written = 0;
            foreach (var pair in _subscriptions)
            {
                foreach (var subscription in pair.Value)
                {
                    if (written >= destination.Length)
                    {
                        return written;
                    }

                    var ownerTypeName = subscription.Owner?.GetType().FullName ?? string.Empty;
                    destination[written++] = new EventSubscriptionDiagnosticRecord(
                        subscription.EventType,
                        ownerTypeName,
                        subscription.Priority);
                }
            }

            return written;
        }

        public EventPublishResult Publish<TEvent>(TEvent payload, EventMeta meta = default)
            where TEvent : IGameEvent
        {
            EnsurePublisherThread();

            var eventType = typeof(TEvent);
            var resolvedMeta = ResolveMeta(meta).WithSequence(++_nextSequence);
            _diagnostics.Record(eventType, resolvedMeta);

            if (!_subscriptions.TryGetValue(eventType, out var list) || list.Count == 0)
            {
                return EventPublishResult.Empty();
            }

            var snapshot = list.ToArray();
            var envelope = new EventEnvelope<TEvent>(payload, resolvedMeta);
            var exceptions = new List<Exception>();
            var delivered = 0;

            foreach (var subscription in snapshot)
            {
                if (!_subscriptions.TryGetValue(eventType, out var currentList) ||
                    !currentList.Exists(item => item.Id == subscription.Id))
                {
                    continue;
                }

                try
                {
                    ((Action<EventEnvelope<TEvent>>)subscription.Handler).Invoke(envelope);
                    delivered++;
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                    _diagnostics.RecordSubscriberException();
                }
            }

            return new EventPublishResult(delivered, exceptions);
        }

        private void EnsurePublisherThread()
        {
            if (!_options.EnforcePublisherThread)
            {
                return;
            }

            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != _options.PublisherThreadId)
            {
                throw new InvalidOperationException(
                    $"EventBus publish must run on publisher thread {_options.PublisherThreadId}; current thread is {currentThreadId}.");
            }
        }

        private static EventMeta ResolveMeta(EventMeta meta)
        {
            return meta.UtcTicks <= 0 ? EventMeta.Create() : meta;
        }

        private static int CompareSubscriptions(Subscription left, Subscription right)
        {
            var priority = right.Priority.CompareTo(left.Priority);
            return priority != 0 ? priority : left.Order.CompareTo(right.Order);
        }
    }
}
