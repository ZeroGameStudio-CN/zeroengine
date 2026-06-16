using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.EnvironmentSystem;

namespace ZeroEngine.Tests.World
{
    [TestFixture]
    [Category("Unit")]
    public sealed class WeatherManagerStateCompatibilityTests
    {
        private GameObject _root;
        private WeatherPresetSO _rainPreset;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_rainPreset != null)
            {
                Object.DestroyImmediate(_rainPreset);
            }
        }

        [Test]
        public void SetWeather_ByTypeWithoutPreset_UpdatesStateClearsCurrentPresetAndClearsPresentation()
        {
            WeatherManager manager = CreateManager();
            RecordingPresentationAdapter adapter = new RecordingPresentationAdapter(manager);
            manager.SetPresentationAdaptersForTests(adapter);
            _rainPreset = CreateRainPreset();
            manager.SetWeather(_rainPreset);

            manager.SetWeather(WeatherType.Storm);

            Assert.That(manager.CurrentWeatherType, Is.EqualTo(WeatherType.Storm));
            Assert.That(manager.CurrentWeather, Is.Null);
            Assert.That(adapter.ApplyCount, Is.EqualTo(1));
            Assert.That(adapter.ClearCount, Is.EqualTo(1));
            Assert.That(adapter.LastClearPreviousType, Is.EqualTo(WeatherType.Rain));
        }

        [Test]
        public void SetWeather_WithPreset_UpdatesStateAndSaveDataBeforePresentationNotification()
        {
            WeatherManager manager = CreateManager();
            RecordingPresentationAdapter adapter = new RecordingPresentationAdapter(manager);
            manager.SetPresentationAdaptersForTests(adapter);
            _rainPreset = CreateRainPreset();

            manager.SetWeather(_rainPreset);

            Assert.That(manager.CurrentWeatherType, Is.EqualTo(WeatherType.Rain));
            Assert.That(manager.CurrentWeather, Is.SameAs(_rainPreset));
            WeatherSaveData saveData = (WeatherSaveData)manager.ExportSaveData();
            Assert.That(saveData.CurrentWeatherType, Is.EqualTo(WeatherType.Rain));
            Assert.That(adapter.ApplyCount, Is.EqualTo(1));
            Assert.That(adapter.LastApplyContext.PreviousWeatherType, Is.EqualTo(WeatherType.Clear));
            Assert.That(adapter.LastApplyContext.CurrentWeatherType, Is.EqualTo(WeatherType.Rain));
            Assert.That(adapter.LastApplyContext.CurrentPreset, Is.SameAs(_rainPreset));
            Assert.That(adapter.LastApplyContext.Immediate, Is.False);
            Assert.That(adapter.ManagerStateAtApply, Is.EqualTo(WeatherType.Rain));
        }

        [Test]
        public void ClearWeather_ClearsStateCurrentPresetAndPresentation()
        {
            WeatherManager manager = CreateManager();
            RecordingPresentationAdapter adapter = new RecordingPresentationAdapter(manager);
            manager.SetPresentationAdaptersForTests(adapter);
            _rainPreset = CreateRainPreset();
            manager.SetWeather(_rainPreset);

            manager.ClearWeather();

            Assert.That(manager.CurrentWeatherType, Is.EqualTo(WeatherType.Clear));
            Assert.That(manager.CurrentWeather, Is.Null);
            Assert.That(adapter.ClearCount, Is.EqualTo(1));
            Assert.That(adapter.LastClearPreviousType, Is.EqualTo(WeatherType.Rain));
        }

        [Test]
        public void SetFollowTarget_ForwardsToFollowTargetPresentationAdapters()
        {
            WeatherManager manager = CreateManager();
            RecordingPresentationAdapter adapter = new RecordingPresentationAdapter(manager);
            manager.SetPresentationAdaptersForTests(adapter);
            var target = new GameObject("WeatherFollowTarget");

            try
            {
                manager.SetFollowTarget(target.transform);

                Assert.That(adapter.FollowTarget, Is.SameAs(target.transform));
                Assert.That(adapter.FollowTargetSetCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        private WeatherManager CreateManager()
        {
            _root = new GameObject("WeatherManagerStateCompatibilityTests");
            return _root.AddComponent<WeatherManager>();
        }

        private static WeatherPresetSO CreateRainPreset()
        {
            WeatherPresetSO preset = ScriptableObject.CreateInstance<WeatherPresetSO>();
            preset.Data.WeatherType = WeatherType.Rain;
            preset.Data.OverrideFog = false;
            preset.Data.VfxPrefab = null;
            preset.Data.AmbientSound = null;
            return preset;
        }

        private sealed class RecordingPresentationAdapter : IWeatherPresentationAdapter, IWeatherFollowTargetAdapter
        {
            private readonly WeatherManager _manager;

            public RecordingPresentationAdapter(WeatherManager manager)
            {
                _manager = manager;
            }

            public int ApplyCount { get; private set; }
            public int ClearCount { get; private set; }
            public WeatherPresentationContext LastApplyContext { get; private set; }
            public WeatherType LastClearPreviousType { get; private set; }
            public WeatherType ManagerStateAtApply { get; private set; }
            public Transform FollowTarget { get; private set; }
            public int FollowTargetSetCount { get; private set; }
            public List<string> Calls { get; } = new List<string>();

            public void ApplyWeatherPresentation(WeatherPresentationContext context)
            {
                ApplyCount++;
                LastApplyContext = context;
                ManagerStateAtApply = _manager.CurrentWeatherType;
                Calls.Add("Apply");
            }

            public void ClearWeatherPresentation(WeatherType previousWeatherType)
            {
                ClearCount++;
                LastClearPreviousType = previousWeatherType;
                Calls.Add("Clear");
            }

            public void SetFollowTarget(Transform target)
            {
                FollowTarget = target;
                FollowTargetSetCount++;
                Calls.Add("FollowTarget");
            }
        }
    }
}
