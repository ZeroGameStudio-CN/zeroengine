using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZeroEngine.Events.Tests
{
    [Category("Unit")]
    public sealed class EventBusTests
    {
        private readonly struct TestEvent : IGameEvent
        {
            public TestEvent(string value) => Value = value;
            public string Value { get; }
        }

        private readonly struct OtherTestEvent : IGameEvent
        {
        }

        private sealed class TestOwner
        {
        }

        [Test]
        public void Publish_DeliversByPriorityThenRegistrationOrder()
        {
            var bus = new EventBus();
            var order = string.Empty;

            bus.Subscribe<TestEvent>(_ => order += "B", new EventSubscriptionOptions(null, priority: 10));
            bus.Subscribe<TestEvent>(_ => order += "C", new EventSubscriptionOptions(null, priority: 10));
            bus.Subscribe<TestEvent>(_ => order += "A", new EventSubscriptionOptions(null, priority: 20));

            var result = bus.Publish(new TestEvent("x"));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.DeliveredCount);
            Assert.AreEqual("ABC", order);
        }

        [Test]
        public void TokenDispose_UnsubscribesHandler()
        {
            var bus = new EventBus();
            var count = 0;
            var token = bus.Subscribe<TestEvent>(_ => count++);

            bus.Publish(new TestEvent("before"));
            token.Dispose();
            bus.Publish(new TestEvent("after"));

            Assert.AreEqual(1, count);
        }

        [Test]
        public void UnsubscribeOwner_RemovesAllOwnedSubscriptions()
        {
            var bus = new EventBus();
            var owner = new object();
            var count = 0;

            bus.Subscribe<TestEvent>(_ => count++, EventSubscriptionOptions.ForOwner(owner));
            bus.Subscribe<TestEvent>(_ => count++, EventSubscriptionOptions.ForOwner(owner));
            var removed = bus.UnsubscribeOwner(owner);
            bus.Publish(new TestEvent("x"));

            Assert.AreEqual(2, removed);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Publish_CollectsSubscriberExceptionsAndContinues()
        {
            var bus = new EventBus();
            var reachedSecond = false;

            bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("boom"));
            bus.Subscribe<TestEvent>(_ => reachedSecond = true);

            var result = bus.Publish(new TestEvent("x"));

            Assert.IsFalse(result.Success);
            Assert.IsTrue(reachedSecond);
            Assert.AreEqual(1, result.Exceptions.Count);
            Assert.AreEqual("boom", result.Exceptions[0].Message);
            Assert.AreEqual(1, bus.Diagnostics.TotalSubscriberExceptions);
        }

        [Test]
        public void Publish_UsesSnapshotWhenSubscriptionChangesDuringPublish()
        {
            var bus = new EventBus();
            var received = string.Empty;
            EventSubscriptionToken secondToken = default;
            secondToken = bus.Subscribe<TestEvent>(_ =>
            {
                received += "A";
                secondToken.Dispose();
            });
            bus.Subscribe<TestEvent>(_ => received += "B");

            bus.Publish(new TestEvent("first"));
            bus.Publish(new TestEvent("second"));

            Assert.AreEqual("ABB", received);
        }

        [Test]
        public void Diagnostics_RecordsLastPublishedEvent()
        {
            var diagnostics = new EventBusDiagnostics(capacity: 4);
            var bus = new EventBus(diagnostics);

            bus.Publish(new TestEvent("tracked"), EventMeta.Create(sourceId: "test.source"));

            Assert.AreEqual(1, diagnostics.TotalPublished);
            Assert.AreEqual(typeof(TestEvent), diagnostics.LastEventType);
            Assert.AreEqual("test.source", diagnostics.LastMeta.SourceId);
        }

        [Test]
        public void Publish_FromDifferentThread_WhenThreadCheckEnabled_Throws()
        {
            var bus = new EventBus();
            Exception caught = null;

            var task = Task.Run(() =>
            {
                try
                {
                    bus.Publish(new TestEvent("background"));
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });

            task.Wait();

            Assert.IsInstanceOf<InvalidOperationException>(caught);
            Assert.That(caught.Message, Does.Contain("publisher thread"));
        }

        [Test]
        public void DeferredQueue_FlushPublishesInEnqueueOrder()
        {
            var bus = new EventBus();
            var queue = new EventBusQueue();
            var received = string.Empty;

            bus.Subscribe<TestEvent>(envelope => received += envelope.Payload.Value);

            queue.Enqueue(new TestEvent("A"));
            queue.Enqueue(new TestEvent("B"));
            var flushed = queue.Flush(bus);

            Assert.AreEqual(2, flushed);
            Assert.AreEqual("AB", received);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void Registry_Reset_ReplacesScopeBus()
        {
            var registry = new EventBusRegistry();
            var oldBus = registry.Get(EventScope.Session);

            var newBus = registry.Reset(EventScope.Session);

            Assert.AreSame(newBus, registry.Get(EventScope.Session));
            Assert.AreNotSame(oldBus, newBus);
        }

        [Test]
        public void Registry_TryGet_DoesNotCreateScopeBus()
        {
            var registry = new EventBusRegistry();

            Assert.IsFalse(registry.TryGet(EventScope.Presentation, out var missingBus));
            Assert.IsNull(missingBus);

            var created = registry.Get(EventScope.Presentation);

            Assert.IsTrue(registry.TryGet(EventScope.Presentation, out var existingBus));
            Assert.AreSame(created, existingBus);
        }

        [Test]
        public void CountSubscriptionsForOwner_DoesNotRequireDiagnosticsToHoldOwner()
        {
            var bus = new EventBus();
            var owner = new object();

            bus.Subscribe<TestEvent>(_ => { }, EventSubscriptionOptions.ForOwner(owner));
            bus.Subscribe<TestEvent>(_ => { }, EventSubscriptionOptions.ForOwner(owner));

            Assert.AreEqual(2, bus.CountSubscriptionsForOwner(owner));
            Assert.AreEqual(2, bus.CountSubscriptions<TestEvent>());
        }

        [Test]
        public void Diagnostics_CopySubscriptionRecords_ReportsTypeOwnerAndPriority()
        {
            var bus = new EventBus();
            var owner = new TestOwner();
            var records = new EventSubscriptionDiagnosticRecord[4];

            bus.Subscribe<TestEvent>(_ => { }, EventSubscriptionOptions.ForOwner(owner, priority: 7));
            var count = bus.CopySubscriptionRecords(records);

            Assert.AreEqual(1, count);
            Assert.AreEqual(typeof(TestEvent), records[0].EventType);
            Assert.AreEqual(typeof(TestOwner).FullName, records[0].OwnerTypeName);
            Assert.AreEqual(7, records[0].Priority);
        }

        [Test]
        public void CountAllSubscriptions_SumsAcrossEventTypes()
        {
            var bus = new EventBus();

            bus.Subscribe<TestEvent>(_ => { });
            bus.Subscribe<TestEvent>(_ => { });
            bus.Subscribe<OtherTestEvent>(_ => { });

            Assert.AreEqual(3, bus.CountAllSubscriptions());
        }

        [Test]
        public void Diagnostics_CopyRecentRecords_ReturnsPublishedMetadata()
        {
            var diagnostics = new EventBusDiagnostics(capacity: 2);
            var bus = new EventBus(diagnostics);
            var records = new EventDiagnosticRecord[2];

            bus.Publish(new TestEvent("A"), EventMeta.Create(sourceId: "first"));
            bus.Publish(new TestEvent("B"), EventMeta.Create(sourceId: "second"));
            var count = diagnostics.CopyRecentRecords(records);

            Assert.AreEqual(2, count);
            Assert.AreEqual(typeof(TestEvent), records[0].EventType);
            Assert.AreEqual("first", records[0].Meta.SourceId);
            Assert.AreEqual("second", records[1].Meta.SourceId);
        }
    }
}
