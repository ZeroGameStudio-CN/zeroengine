using NUnit.Framework;
using ZeroEngine.Events.Editor;

namespace ZeroEngine.Events.Tests
{
    public sealed class EventBusObservatoryTests
    {
        private readonly struct TestEvent : IGameEvent
        {
            public TestEvent(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        [Test]
        public void SnapshotBuilder_CapturesScopesSubscriptionsRecentEventsAndFilters()
        {
            var bus = new EventBus();
            bus.Subscribe<TestEvent>(
                _ => { },
                EventSubscriptionOptions.ForOwner(this, priority: 7));
            bus.Publish(new TestEvent("ignored"), EventMeta.Create(EventScope.Session, sourceId: "combat"));
            bus.Publish(new TestEvent("matched"), EventMeta.Create(EventScope.Session, sourceId: "quest"));

            var snapshot = EventBusDiagnosticsSnapshotBuilder.Capture(
                new[]
                {
                    EventBusDiagnosticsScopeSource.Created("Session", bus),
                    EventBusDiagnosticsScopeSource.Missing("Scene")
                },
                new EventBusDiagnosticsQuery(recentEventCount: 8, subscriptionCount: 8));
            var filteredSnapshot = EventBusDiagnosticsSnapshotBuilder.Capture(
                new[]
                {
                    EventBusDiagnosticsScopeSource.Created("Session", bus)
                },
                new EventBusDiagnosticsQuery(recentEventCount: 8, subscriptionCount: 8, filter: "quest"));

            Assert.That(snapshot.Scopes.Count, Is.EqualTo(2));
            Assert.That(snapshot.Scopes[0].Name, Is.EqualTo("Session"));
            Assert.That(snapshot.Scopes[0].IsCreated, Is.True);
            Assert.That(snapshot.Scopes[0].TotalPublished, Is.EqualTo(2));
            Assert.That(snapshot.Scopes[0].TotalSubscriptions, Is.EqualTo(1));
            Assert.That(snapshot.Scopes[0].Subscriptions.Count, Is.EqualTo(1));
            Assert.That(snapshot.Scopes[0].Subscriptions[0].EventTypeName, Is.EqualTo(nameof(TestEvent)));
            Assert.That(snapshot.Scopes[0].Subscriptions[0].OwnerTypeName, Does.Contain(nameof(EventBusObservatoryTests)));
            Assert.That(snapshot.Scopes[0].Subscriptions[0].Priority, Is.EqualTo(7));
            Assert.That(filteredSnapshot.Scopes[0].RecentEvents.Count, Is.EqualTo(1));
            Assert.That(filteredSnapshot.Scopes[0].RecentEvents[0].EventTypeName, Is.EqualTo(nameof(TestEvent)));
            Assert.That(filteredSnapshot.Scopes[0].RecentEvents[0].SourceId, Is.EqualTo("quest"));
            Assert.That(snapshot.Scopes[1].Name, Is.EqualTo("Scene"));
            Assert.That(snapshot.Scopes[1].IsCreated, Is.False);
        }

        [Test]
        public void TextAndJsonExport_IncludeHighSignalDiagnostics()
        {
            var bus = new EventBus();
            bus.Subscribe<TestEvent>(
                _ => throw new System.InvalidOperationException("boom"),
                EventSubscriptionOptions.ForOwner(this, priority: 3));
            bus.Publish(new TestEvent("matched"), EventMeta.Create(EventScope.Session, sourceId: "quest"));

            var snapshot = EventBusDiagnosticsSnapshotBuilder.Capture(
                new[] { EventBusDiagnosticsScopeSource.Created("Session", bus) },
                new EventBusDiagnosticsQuery(recentEventCount: 4, subscriptionCount: 4));

            var text = EventBusDiagnosticsTextFormatter.Format(snapshot);
            var json = EventBusDiagnosticsJsonExporter.ToJson(snapshot);

            Assert.That(text, Does.Contain("Session"));
            Assert.That(text, Does.Contain("exceptions=1"));
            Assert.That(text, Does.Contain(nameof(TestEvent)));
            Assert.That(text, Does.Contain(nameof(EventBusObservatoryTests)));
            Assert.That(text, Does.Contain("quest"));
            Assert.That(json, Does.Contain("\"scopes\""));
            Assert.That(json, Does.Contain("\"name\":\"Session\""));
            Assert.That(json, Does.Contain("\"sourceId\":\"quest\""));
            Assert.That(json, Does.Contain("\"ownerTypeName\""));
        }

        [Test]
        public void EventObservatoryPanel_LivesInReusableZeroEngineEditorNamespace()
        {
            Assert.That(typeof(EventObservatoryPanel).Namespace, Is.EqualTo("ZeroEngine.Events.Editor"));
        }
    }
}
