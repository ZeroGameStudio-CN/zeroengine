using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Events.Tests
{
    [Category("Unit")]
    public sealed class EventChannelTests
    {
        [Test]
        public void IntEventChannel_RaisesValueToListener()
        {
            var channel = ScriptableObject.CreateInstance<ZeroEngine.Events.Unity.IntEventChannel>();
            var received = 0;
            var listener = new RecordingIntListener(value => received = value);

            channel.Register(listener);
            channel.Raise(42);

            Assert.AreEqual(42, received);
        }

        [Test]
        public void EventChannel_UnregisterStopsDelivery()
        {
            var channel = ScriptableObject.CreateInstance<ZeroEngine.Events.Unity.StringEventChannel>();
            var received = 0;
            var listener = new RecordingStringListener(_ => received++);

            channel.Register(listener);
            channel.Unregister(listener);
            channel.Raise("x");

            Assert.AreEqual(0, received);
        }

        [Test]
        public void EventChannel_RegisterSameListenerTwice_DeliversOnce()
        {
            var channel = ScriptableObject.CreateInstance<ZeroEngine.Events.Unity.IntEventChannel>();
            var received = 0;
            var listener = new RecordingIntListener(_ => received++);

            channel.Register(listener);
            channel.Register(listener);
            channel.Raise(7);

            Assert.AreEqual(1, channel.ListenerCount);
            Assert.AreEqual(1, received);
        }

        [Test]
        public void EventChannel_Raise_PrunesDestroyedUnityListeners()
        {
            var channel = ScriptableObject.CreateInstance<ZeroEngine.Events.Unity.IntEventChannel>();
            var gameObject = new GameObject("EventChannelTestListener");
            var listener = gameObject.AddComponent<RecordingMonoIntListener>();

            channel.Register(listener);
            Object.DestroyImmediate(gameObject);
            channel.Raise(7);

            Assert.AreEqual(0, channel.ListenerCount);
        }

        private sealed class RecordingIntListener : ZeroEngine.Events.Unity.IEventChannelListener<int>
        {
            private readonly System.Action<int> _onValue;
            public RecordingIntListener(System.Action<int> onValue) => _onValue = onValue;
            public void OnEventRaised(int value) => _onValue(value);
        }

        private sealed class RecordingStringListener : ZeroEngine.Events.Unity.IEventChannelListener<string>
        {
            private readonly System.Action<string> _onValue;
            public RecordingStringListener(System.Action<string> onValue) => _onValue = onValue;
            public void OnEventRaised(string value) => _onValue(value);
        }

        private sealed class RecordingMonoIntListener : MonoBehaviour, ZeroEngine.Events.Unity.IEventChannelListener<int>
        {
            public int ReceivedCount { get; private set; }
            public void OnEventRaised(int value) => ReceivedCount++;
        }
    }
}
