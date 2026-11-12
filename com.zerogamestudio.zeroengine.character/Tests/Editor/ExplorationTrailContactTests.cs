using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Character.Exploration;

namespace ZeroEngine.Character.Tests
{
    [Category("Unit")]
    public sealed class ExplorationTrailContactTests
    {
        [Test]
        public void Trail_CornerCapacityAndRelocation_KeepOnlyWalkedGeometry()
        {
            var trail = new ExplorationTrailBuffer(3);
            trail.Reset(Vector3.zero);
            trail.TryAppend(Vector3.right);
            trail.TryAppend(new Vector3(1f, 0f, 1f));
            Assert.That(trail.TrySample(1.5, out var point), Is.True);
            Assert.That(Vector3.Distance(point, new Vector3(1f, 0f, .5f)), Is.LessThan(.00001f));
            Assert.That(trail.TryFindClosestDistance(new Vector3(1f, 2f, .5f), out double distance), Is.True);
            Assert.That(distance, Is.EqualTo(1.5).Within(.00001));
            trail.TryAppend(new Vector3(2f, 0f, 1f));
            Assert.That(trail.Count, Is.EqualTo(3));
            Assert.That(trail.OldestDistance, Is.EqualTo(1));
            Assert.That(trail.TryAppend(new Vector3(100f, 0f, 0f)), Is.False);
            Assert.That(trail.Count, Is.EqualTo(3));
            Assert.That(trail.Reset(new Vector3(float.NaN, 0f, 0f)), Is.False);
            Assert.That(trail.Count, Is.EqualTo(3));
            trail.Reset(new Vector3(50f, 0f, 0f));
            Assert.That(trail.Count, Is.EqualTo(1));
            Assert.That(trail.TrySample(-10, out point), Is.True);
            Assert.That(point, Is.EqualTo(new Vector3(50f, 0f, 0f)));
        }

        [Test]
        public void Trail_LocalProgressRange_RejectsAnOlderCrossingAndClampsOvershoot()
        {
            var trail = new ExplorationTrailBuffer();
            trail.Reset(Vector3.zero);
            trail.TryAppend(Vector3.right);
            trail.TryAppend(new Vector3(1f, 0f, 1f));
            trail.TryAppend(new Vector3(0f, 0f, 1f));
            trail.TryAppend(Vector3.zero);
            Assert.That(trail.TryFindClosestDistance(Vector3.zero, 2.5, 3.75, out double distance), Is.True);
            Assert.That(distance, Is.EqualTo(3.75).Within(.00001));
            Assert.That(trail.TryFindClosestDistance(Vector3.zero, 3.75, 4.0, out distance), Is.True);
            Assert.That(distance, Is.EqualTo(4.0).Within(.00001));
            Assert.That(trail.TryFindClosestDistance(Vector3.zero, 5.0, 6.0, out _), Is.False);
        }

        [Test]
        public void Contacts_DistancePauseTeleportAndOverflow_NeverReplayStaleTravel()
        {
            var sampler = new FootstepDistanceSampler();
            var contacts = new List<Vector2>(8);
            sampler.Advance(Vector2.zero, true, .5f, contacts);
            sampler.Advance(new Vector2(1.3f, 0f), true, .5f, contacts);
            Assert.That(contacts.Count, Is.EqualTo(2));
            Assert.That(Vector2.Distance(contacts[0], new Vector2(.5f, 0f)), Is.LessThan(.00001f));
            sampler.Advance(new Vector2(100f, 0f), true, .5f, contacts);
            Assert.That(contacts, Is.Empty);
            sampler.Advance(new Vector2(100f, 0f), false, .5f, contacts);
            sampler.Advance(new Vector2(100f, 0f), true, .5f, contacts);
            Assert.That(contacts, Is.Empty);
            sampler.Advance(new Vector2(104f, 0f), true, .05f, contacts);
            Assert.That(contacts.Count, Is.EqualTo(8));
            sampler.Advance(new Vector2(104f, 0f), true, .05f, contacts);
            Assert.That(contacts, Is.Empty, "A capped frame must not leave a future backlog.");
        }

        [Test]
        public void Contacts_ReversalAndInvalidSpacing_DoNotSpendAnOldPartialStride()
        {
            var sampler = new FootstepDistanceSampler();
            var contacts = new List<Vector2>();
            sampler.Advance(Vector2.zero, true, .5f, contacts);
            sampler.Advance(new Vector2(.4f, 0f), true, .5f, contacts);
            sampler.Advance(new Vector2(.2f, 0f), true, .5f, contacts);
            Assert.That(contacts, Is.Empty);
            sampler.Advance(Vector2.zero, true, .5f, contacts);
            Assert.That(contacts, Is.Empty);
            sampler.Advance(new Vector2(-.2f, 0f), true, .5f, contacts);
            Assert.That(contacts.Count, Is.EqualTo(1));
            Assert.That(contacts[0].x, Is.EqualTo(-.1f).Within(.00001f));
            sampler.Advance(Vector2.zero, true, float.NaN, contacts);
            Assert.That(contacts, Is.Empty);
            sampler.Advance(new Vector2(5f, 0f), true, .5f, contacts);
            Assert.That(contacts, Is.Empty, "Invalid input resets the sampling origin.");
        }
    }
}
