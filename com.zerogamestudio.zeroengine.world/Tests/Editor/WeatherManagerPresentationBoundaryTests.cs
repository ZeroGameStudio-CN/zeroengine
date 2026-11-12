using NUnit.Framework;
using UnityEngine;
using ZeroEngine.EnvironmentSystem;

namespace ZeroEngine.World.Tests.Editor
{
    public sealed class WeatherManagerPresentationBoundaryTests
    {
        private bool _fogEnabled;
        private Color _fogColor;
        private float _fogDensity;
        private GameObject _root;
        private WeatherPresetSO _preset;

        [SetUp]
        public void SetUp()
        {
            _fogEnabled = RenderSettings.fog;
            _fogColor = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;
            _preset = ScriptableObject.CreateInstance<WeatherPresetSO>();
            _preset.Data = new WeatherPresetData
            {
                WeatherType = WeatherType.Rain,
                OverrideFog = true,
                EnableFog = true,
                FogColor = Color.magenta,
                FogDensity = 0.173f,
                TransitionDuration = 0f
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_preset != null)
            {
                Object.DestroyImmediate(_preset);
            }

            RenderSettings.fog = _fogEnabled;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogDensity = _fogDensity;
        }

        [Test]
        public void SetAndClearWeather_WithoutAdapter_PreservesPresentationAndOwnsState()
        {
            WeatherManager manager = CreateManager();
            RenderSettings.fog = false;
            RenderSettings.fogColor = Color.cyan;
            RenderSettings.fogDensity = 0.031f;

            manager.SetWeather(_preset);

            Assert.That(manager.CurrentWeather, Is.SameAs(_preset));
            Assert.That(manager.CurrentWeatherType, Is.EqualTo(WeatherType.Rain));
            Assert.That(manager.HasPresentationAdapter, Is.False);
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(RenderSettings.fogColor, Is.EqualTo(Color.cyan));
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.031f));

            manager.ClearWeather();

            Assert.That(manager.CurrentWeather, Is.Null);
            Assert.That(manager.CurrentWeatherType, Is.EqualTo(WeatherType.Clear));
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(RenderSettings.fogColor, Is.EqualTo(Color.cyan));
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.031f));
        }

        [Test]
        public void SetAndClearWeather_WithExplicitAdapter_DelegatesExactContext()
        {
            WeatherManager manager = CreateManager();
            var adapter = _root.AddComponent<RecordingPresentationAdapter>();
            manager.ConfigurePresentationAdapterForTests(adapter);

            manager.SetWeather(_preset);

            Assert.That(manager.HasPresentationAdapter, Is.True);
            Assert.That(adapter.ApplyCount, Is.EqualTo(1));
            Assert.That(adapter.LastContext.PreviousWeatherType, Is.EqualTo(WeatherType.Clear));
            Assert.That(adapter.LastContext.CurrentWeatherType, Is.EqualTo(WeatherType.Rain));
            Assert.That(adapter.LastContext.CurrentPreset, Is.SameAs(_preset));
            Assert.That(adapter.LastContext.Immediate, Is.False);

            manager.ClearWeather();

            Assert.That(adapter.ClearCount, Is.EqualTo(1));
            Assert.That(adapter.LastClearedType, Is.EqualTo(WeatherType.Rain));
        }

        [Test]
        public void ConfigurePresentationAdapter_InvalidComponent_FailsClosed()
        {
            WeatherManager manager = CreateManager();
            var invalid = _root.AddComponent<InvalidPresentationBehaviour>();

            Assert.Throws<System.InvalidOperationException>(
                () => manager.ConfigurePresentationAdapterForTests(invalid));
            Assert.That(manager.HasPresentationAdapter, Is.False);
        }

        private WeatherManager CreateManager()
        {
            _root = new GameObject("WeatherManagerPresentationBoundaryTests");
            return _root.AddComponent<WeatherManager>();
        }

        private sealed class RecordingPresentationAdapter : MonoBehaviour,
            IWeatherPresentationAdapter
        {
            public int ApplyCount { get; private set; }
            public int ClearCount { get; private set; }
            public WeatherPresentationContext LastContext { get; private set; }
            public WeatherType LastClearedType { get; private set; }

            public void ApplyWeatherPresentation(WeatherPresentationContext context)
            {
                ApplyCount++;
                LastContext = context;
            }

            public void ClearWeatherPresentation(WeatherType previousWeatherType)
            {
                ClearCount++;
                LastClearedType = previousWeatherType;
            }
        }

        private sealed class InvalidPresentationBehaviour : MonoBehaviour
        {
        }
    }
}
