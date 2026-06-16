using NUnit.Framework;
using UnityEngine;
using ZeroEngine.EnvironmentSystem;
using ZeroEngine.Timing;

namespace ZeroEngine.Tests.World
{
    [TestFixture]
    [Category("Unit")]
    public sealed class WorldClockTimeManagerTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            TimeControlLocator.ResetForTests();
            _root = new GameObject("WorldClockTimeManagerTests");
        }

        [TearDown]
        public void TearDown()
        {
            TimeControlLocator.ResetForTests();
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void AdvanceUnscaledSeconds_UsesWorldClockScaleInGraduatedMode()
        {
            TimeManager manager = CreateGraduatedManager();
            manager.SetTime(8f);
            manager.TimeScale = 60f;
            TimeControlLocator.Service.SetBaseScale(TimeDomainIds.WorldClock, 0.5f);

            manager.AdvanceUnscaledSecondsForTests(60f);

            Assert.That(manager.CurrentHour, Is.EqualTo(8.5f).Within(0.0001f));
        }

        [Test]
        public void PresentationScale_DoesNotAffectWorldClockInGraduatedMode()
        {
            TimeManager manager = CreateGraduatedManager();
            manager.SetTime(8f);
            manager.TimeScale = 60f;
            TimeControlLocator.Service.SetBaseScale(TimeDomainIds.Presentation, 0.1f);
            TimeControlLocator.Service.SetBaseScale(TimeDomainIds.WorldClock, 1f);

            manager.AdvanceUnscaledSecondsForTests(60f);

            Assert.That(manager.CurrentHour, Is.EqualTo(9f).Within(0.0001f));
        }

        [Test]
        public void Pause_StopsGraduatedWorldClock()
        {
            TimeManager manager = CreateGraduatedManager();
            manager.SetTime(8f);
            manager.TimeScale = 60f;
            TimeControlLocator.Service.SetBaseScale(TimeDomainIds.WorldClock, 2f);

            manager.Pause();
            manager.AdvanceUnscaledSecondsForTests(60f);

            Assert.That(manager.CurrentHour, Is.EqualTo(8f).Within(0.0001f));
        }

        [Test]
        public void AdvanceHours_WrapsAcrossMultipleDaysAndNegativeHours()
        {
            TimeManager manager = CreateGraduatedManager();

            manager.SetTime(23f);
            manager.AdvanceHours(49.5f);
            Assert.That(manager.CurrentHour, Is.EqualTo(0.5f).Within(0.0001f));

            manager.AdvanceHours(-2f);
            Assert.That(manager.CurrentHour, Is.EqualTo(22.5f).Within(0.0001f));
        }

        private TimeManager CreateGraduatedManager()
        {
            TimeManager manager = _root.AddComponent<TimeManager>();
            manager.DeltaPolicy = WorldClockDeltaPolicy.UnscaledWorldClockDomain;
            return manager;
        }
    }
}
