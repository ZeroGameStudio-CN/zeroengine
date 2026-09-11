using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Activity.Unity;

namespace ZeroEngine.Activity.Tests
{
    public sealed class ActivityUnityBridgeTests
    {
        private GameObject root;
        [SetUp] public void SetUp() => root = new GameObject("Activity contract world");
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        private T Child<T>(string name) where T : Component
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform);
            return child.AddComponent<T>();
        }

        [Test]
        public void ObserverMotion_ChangesLevelsWithoutDeactivatingEntity()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            var observer = Child<ActivityObserver>("observer");
            observer.Configure(world);
            var agent = Child<ActivityAgent>("agent");
            ActivityLevel applied = ActivityLevel.Full;
            double time = 0;
            agent.Configure(world, new ActivityPolicy(10, 30, 5, 0, 0.25), () => time,
                () => ActivityLevel.Dormant, level => applied = level);
            observer.transform.position = new Vector3(100, 0, 0);
            world.Tick();
            Assert.That(applied, Is.EqualTo(ActivityLevel.Dormant));
            Assert.That(agent.gameObject.activeSelf, Is.True);
            Assert.That(agent.enabled, Is.True);
            observer.transform.position = Vector3.zero;
            time++;
            world.Tick();
            Assert.That(applied, Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void MissingObservers_FailsOpen_AndKeepAliveIsImmediate()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            var agent = Child<ActivityAgent>("agent");
            agent.Configure(world, new ActivityPolicy(1, 2, 0, 0, 0.25), () => 0,
                () => ActivityLevel.Dormant, _ => { });
            world.Tick();
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Full));
            var observer = Child<ActivityObserver>("observer");
            observer.transform.position = new Vector3(100, 0, 0);
            observer.Configure(world);
            world.Tick();
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Dormant));
            using (agent.KeepActive()) Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Full));
            world.Tick();
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Dormant));
            world.Unregister(observer);
            world.Tick();
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void WorldDisable_DoesNotDispatchQueuedWork()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            world.Work.Enqueue("test", () => Assert.Fail("Disabled world dispatched work."));
            world.enabled = false;
            Assert.That(world.Tick(), Is.Zero);
            world.Work.Resume();
            Assert.That(world.Work.Enqueue("late", () => Assert.Fail()), Is.False);
            Assert.That(world.Work.Pump(1, 1), Is.Zero);
        }

        [Test]
        public void ReentrantWakeAndWorldTick_DoNotRecursivelyApply()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            var agent = Child<ActivityAgent>("agent");
            int applications = 0;
            agent.Configure(world, new ActivityPolicy(1, 2, 0, 0, 0.25), () => 0,
                () => ActivityLevel.Full, _ => { applications++; agent.Wake(); world.Tick(); });
            Assert.That(applications, Is.EqualTo(1));
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void InvalidDecisionClock_AppliesTheFullFallbackImmediately()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            var observer = Child<ActivityObserver>("observer");
            observer.transform.position = new Vector3(100, 0, 0);
            observer.Configure(world);
            var agent = Child<ActivityAgent>("agent");
            ActivityLevel applied = ActivityLevel.Full;
            agent.Configure(world, new ActivityPolicy(1, 2, 0, 0, 0.25), () => 0,
                () => ActivityLevel.Dormant, level => applied = level);
            world.Tick();
            Assert.That(applied, Is.EqualTo(ActivityLevel.Dormant));
            Assert.That(agent.TakeDecisionTick(double.NaN), Is.True);
            Assert.That(applied, Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void InvalidReconfiguration_DoesNotRemoveExistingWorldRegistration()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            var other = Child<ActivityWorldDriver>("other world");
            var observer = Child<ActivityObserver>("observer");
            observer.transform.position = new Vector3(100, 0, 0);
            observer.Configure(world);
            var agent = Child<ActivityAgent>("agent");
            agent.Configure(world, new ActivityPolicy(1, 2, 0, 0, 0.25), () => 0,
                () => ActivityLevel.Dormant, _ => { });
            world.Tick();
            Assert.Throws<ArgumentException>(() => agent.Configure(other, default, () => 0,
                () => ActivityLevel.Full, _ => { }));
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Dormant));
            observer.transform.position = Vector3.zero;
            world.Tick();
            Assert.That(agent.Level, Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void FailedApply_RetriesFullAndDoesNotInterruptOtherParticipants()
        {
            var world = root.AddComponent<ActivityWorldDriver>();
            var observer = Child<ActivityObserver>("observer");
            observer.transform.position = new Vector3(100, 0, 0);
            observer.Configure(world);
            var first = Child<ActivityAgent>("first");
            var second = Child<ActivityAgent>("second");
            ActivityLevel applied = ActivityLevel.Full;
            first.Configure(world, new ActivityPolicy(1, 2, 0, 0, 0.25), () => 0,
                () => ActivityLevel.Dormant, level =>
                {
                    if (level == ActivityLevel.Dormant) throw new InvalidOperationException("fixture apply failure");
                    applied = level;
                });
            second.Configure(world, new ActivityPolicy(1, 2, 0, 0, 0.25), () => 0,
                () => ActivityLevel.Dormant, _ => { });
            LogAssert.Expect(LogType.Exception, new Regex("fixture apply failure"));
            Assert.DoesNotThrow(() => world.Tick());
            Assert.That(first.IsFaulted, Is.True);
            Assert.That(applied, Is.EqualTo(ActivityLevel.Full));
            Assert.That(second.Level, Is.EqualTo(ActivityLevel.Dormant));
        }
    }
}
