using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Core;

namespace ZeroEngine.Minimap.Tests.Editor
{
    public sealed class MinimapControllerTests
    {
        private GameObject _controllerObject;
        private MinimapController _controller;

        [SetUp]
        public void SetUp()
        {
            DestroySingletonObjects<MinimapController>();
            MinimapMarkerManager.ClearAll();

            _controllerObject = new GameObject("ZE_MinimapController");
            _controller = _controllerObject.AddComponent<MinimapController>();
            InvokeLifecycleIfPresent(_controller, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (_controller != null)
            {
                InvokeLifecycleIfPresent(_controller, "OnDisable");
            }

            if (_controllerObject != null)
            {
                Object.DestroyImmediate(_controllerObject);
                _controllerObject = null;
            }

            DestroySingletonObjects<MinimapController>();
            MinimapMarkerManager.ClearAll();
        }

        [Test]
        public void MarkerRegistration_ForwardsThroughControllerEventStream()
        {
            var events = new List<(MinimapEventType Type, MinimapMarker Marker)>();
            _controller.OnMinimapEvent += args => events.Add((args.Type, args.Marker));

            var markerObject = new GameObject("QuestMarker");
            markerObject.SetActive(false);
            var marker = markerObject.AddComponent<MinimapMarker>();
            marker.Setup(MinimapMarkerType.Quest);

            try
            {
                markerObject.SetActive(true);
                InvokeLifecycleIfPresent(marker, "OnEnable");

                Assert.That(MinimapMarkerManager.MarkerCount, Is.EqualTo(1));
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].Type, Is.EqualTo(MinimapEventType.MarkerAdded));
                Assert.That(events[0].Marker, Is.SameAs(marker));

                InvokeLifecycleIfPresent(marker, "OnDisable");
                markerObject.SetActive(false);

                Assert.That(MinimapMarkerManager.MarkerCount, Is.EqualTo(0));
                Assert.That(events, Has.Count.EqualTo(2));
                Assert.That(events[1].Type, Is.EqualTo(MinimapEventType.MarkerRemoved));
                Assert.That(events[1].Marker, Is.SameAs(marker));
            }
            finally
            {
                Object.DestroyImmediate(markerObject);
            }
        }

        private static void DestroySingletonObjects<T>() where T : MonoBehaviour
        {
            foreach (var instance in Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(instance.gameObject);
            }

            typeof(MonoSingleton<T>)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, null);
        }

        private static void InvokeLifecycleIfPresent(MonoBehaviour target, string methodName)
        {
            target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(target, null);
        }
    }
}
