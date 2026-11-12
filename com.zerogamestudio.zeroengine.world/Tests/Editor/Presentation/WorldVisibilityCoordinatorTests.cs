using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.World.Presentation;

namespace ZeroEngine.World.Tests.Editor.Presentation
{
    public sealed class WorldVisibilityCoordinatorTests
    {
        [TestCase(null, "channel", "source", "targetId")]
        [TestCase(" ", "channel", "source", "targetId")]
        [TestCase("target", null, "source", "channelId")]
        [TestCase("target", "\t", "source", "channelId")]
        [TestCase("target", "channel", null, "sourceId")]
        [TestCase("target", "channel", "\r\n", "sourceId")]
        public void Constructor_MissingRequiredId_ThrowsArgumentException(
            string targetId,
            string channelId,
            string sourceId,
            string expectedParameterName)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new WorldVisibilityRequest(targetId, channelId, sourceId, null, 1f));

            Assert.That(exception.ParamName, Is.EqualTo(expectedParameterName));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        public void Constructor_InvalidVisibility_ThrowsArgumentOutOfRangeException(float visibility)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WorldVisibilityRequest("target", "channel", "source", null, visibility));
        }

        [Test]
        public void Constructor_ValidValues_NormalizesIdentifiersAndReason()
        {
            var request = new WorldVisibilityRequest(
                " roof ",
                " interior ",
                " apothecary-zone ",
                " entering interior ",
                0.5f);

            Assert.That(request.TargetId, Is.EqualTo("roof"));
            Assert.That(request.ChannelId, Is.EqualTo("interior"));
            Assert.That(request.SourceId, Is.EqualTo("apothecary-zone"));
            Assert.That(request.Reason, Is.EqualTo("entering interior"));
            Assert.That(request.Visibility, Is.EqualTo(0.5f));
        }

        [Test]
        public void Acquire_SameChannelUsesMinimumAndDifferentChannelsMultiply()
        {
            var coordinator = new WorldVisibilityCoordinator();
            using var interiorA = coordinator.Acquire(Request("roof", "interior", "zone-a", 0.7f));
            using var interiorB = coordinator.Acquire(Request("roof", "interior", "zone-b", 0.4f));
            using var occlusion = coordinator.Acquire(Request("roof", "camera-occlusion", "camera", 0.5f));

            Assert.That(coordinator.GetEffectiveSnapshot(" roof ").Visibility, Is.EqualTo(0.2f).Within(0.00001f));
        }

        [Test]
        public void Dispose_OverlappingSources_RestoresCompositeVisibilityPrecisely()
        {
            var coordinator = new WorldVisibilityCoordinator();
            using var interiorA = coordinator.Acquire(Request("roof", "interior", "zone-a", 0.7f));
            var interiorB = coordinator.Acquire(Request("roof", "interior", "zone-b", 0.4f));
            var occlusion = coordinator.Acquire(Request("roof", "camera-occlusion", "camera", 0.5f));

            interiorB.Dispose();
            Assert.That(coordinator.GetEffectiveSnapshot("roof").Visibility, Is.EqualTo(0.35f).Within(0.00001f));

            occlusion.Dispose();
            Assert.That(coordinator.GetEffectiveSnapshot("roof").Visibility, Is.EqualTo(0.7f).Within(0.00001f));
        }

        [Test]
        public void RequestWithoutEffectiveChange_DoesNotRaiseEventOrAffectOtherTarget()
        {
            var coordinator = new WorldVisibilityCoordinator();
            using var roof = coordinator.Acquire(Request("roof", "interior", "winner", 0.4f));
            using var wall = coordinator.Acquire(Request("wall", "interior", "wall", 0.6f));
            var changeCount = 0;
            coordinator.EffectiveVisibilityChanged += (_, _) => changeCount++;

            var ineffective = coordinator.Acquire(Request("roof", "interior", "ineffective", 0.8f));
            ineffective.Dispose();

            Assert.That(changeCount, Is.Zero);
            Assert.That(coordinator.GetEffectiveSnapshot("roof").Visibility, Is.EqualTo(0.4f));
            Assert.That(coordinator.GetEffectiveSnapshot("wall").Visibility, Is.EqualTo(0.6f));
        }

        [Test]
        public void FullVisibilityRequest_DoesNotRaiseEventWhenEffectiveValueRemainsOne()
        {
            var coordinator = new WorldVisibilityCoordinator();
            var changeCount = 0;
            coordinator.EffectiveVisibilityChanged += (_, _) => changeCount++;

            var lease = coordinator.Acquire(Request("roof", "interior", "source", 1f));
            lease.Dispose();

            Assert.That(changeCount, Is.Zero);
            Assert.That(coordinator.GetEffectiveSnapshot("roof").Visibility, Is.EqualTo(1f));
        }

        [Test]
        public void CopyActiveSnapshots_SortsByTargetChannelThenTokenAndClearsDestination()
        {
            var coordinator = new WorldVisibilityCoordinator();
            using var zTarget = coordinator.Acquire(Request("z-target", "a-channel", "z", 0.9f));
            using var bChannel = coordinator.Acquire(Request("a-target", "b-channel", "b", 0.8f));
            using var aChannelFirst = coordinator.Acquire(Request("a-target", "a-channel", "a-one", 0.7f));
            using var aChannelSecond = coordinator.Acquire(Request("a-target", "a-channel", "a-two", 0.6f));
            var results = new List<WorldVisibilityRequestSnapshot> { default };

            coordinator.CopyActiveSnapshots(results);

            Assert.That(results, Has.Count.EqualTo(4));
            Assert.That(results[0].TokenId, Is.EqualTo(aChannelFirst.TokenId));
            Assert.That(results[1].TokenId, Is.EqualTo(aChannelSecond.TokenId));
            Assert.That(results[2].TokenId, Is.EqualTo(bChannel.TokenId));
            Assert.That(results[3].TokenId, Is.EqualTo(zTarget.TokenId));
        }

        [Test]
        public void Dispose_CalledTwice_IsIdempotent()
        {
            var coordinator = new WorldVisibilityCoordinator();
            var lease = coordinator.Acquire(Request("roof", "interior", "source", 0.4f));
            var changeCount = 0;
            coordinator.EffectiveVisibilityChanged += (_, _) => changeCount++;

            lease.Dispose();
            lease.Dispose();

            Assert.That(lease.IsReleased, Is.True);
            Assert.That(coordinator.ActiveRequestCount, Is.Zero);
            Assert.That(coordinator.GetEffectiveSnapshot("roof").Visibility, Is.EqualTo(1f));
            Assert.That(changeCount, Is.EqualTo(1));
        }

        [Test]
        public void Acquire_SubscriberThrows_RollsBackRequestAndRestoresVisibility()
        {
            var coordinator = new WorldVisibilityCoordinator();
            using var baseline = coordinator.Acquire(
                Request("roof", "interior", "baseline", 0.6f));
            Action<WorldVisibilitySnapshot, WorldVisibilitySnapshot> handler =
                (_, _) => throw new InvalidOperationException("subscriber failed");
            coordinator.EffectiveVisibilityChanged += handler;

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.Acquire(Request("roof", "camera", "camera", 0.5f)));
            coordinator.EffectiveVisibilityChanged -= handler;

            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(1));
            Assert.That(coordinator.GetEffectiveSnapshot("roof").Visibility, Is.EqualTo(0.6f));
        }

        [Test]
        public void ChangeDispatch_ReentrantAcquireAndRelease_AreRejectedWithoutLosingLease()
        {
            var coordinator = new WorldVisibilityCoordinator();
            var baseline = coordinator.Acquire(Request("roof", "interior", "baseline", 0.6f));
            Action<WorldVisibilitySnapshot, WorldVisibilitySnapshot> handler = (_, _) =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                    coordinator.Acquire(Request("roof", "weather", "nested", 0.5f)));
                Assert.Throws<InvalidOperationException>(() => baseline.Dispose());
            };
            coordinator.EffectiveVisibilityChanged += handler;

            var camera = coordinator.Acquire(Request("roof", "camera", "camera", 0.5f));
            coordinator.EffectiveVisibilityChanged -= handler;

            Assert.That(baseline.IsReleased, Is.False);
            Assert.That(coordinator.ActiveRequestCount, Is.EqualTo(2));

            baseline.Dispose();
            camera.Dispose();
        }

        private static WorldVisibilityRequest Request(
            string targetId,
            string channelId,
            string sourceId,
            float visibility)
        {
            return new WorldVisibilityRequest(
                targetId,
                channelId,
                sourceId,
                null,
                visibility);
        }
    }
}
