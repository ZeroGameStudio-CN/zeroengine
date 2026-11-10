using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.FogOfWar.Unity;
using Object = UnityEngine.Object;

namespace ZeroEngine.FogOfWar.Tests
{
    // Core contract: explicitly tracked observations preserve synchronous native scene state.
    public sealed class SceneObjectSnapshotTests
    {
        private readonly List<GameObject> owned = new();
        private SceneObjectSnapshot2D snapshot;
        [SetUp] public void CreateObserver()
        {
            Assert.That(SceneObjectSnapshot2D.TryCreate(out snapshot, out string reason), Is.True, reason);
        }
        [TearDown] public void Cleanup()
        {
            snapshot?.Dispose();
            foreach (var item in owned) if (item) Object.DestroyImmediate(item);
            owned.Clear();
        }
        private BoxCollider2D Item(Transform parent = null, bool rect = false)
        {
            var node = rect ? new GameObject("Synthetic observed rect", typeof(RectTransform)) : new GameObject("Synthetic observed object");
            owned.Add(node); if (parent) node.transform.SetParent(parent, false);
            return node.AddComponent<BoxCollider2D>();
        }
        private void Match(SceneObjectSnapshot2D.Target target, Component item)
        {
            Assert.That(snapshot.TryRead(target, out var state), Is.True, snapshot.Failure);
            Assert.That(state.Alive, Is.EqualTo((bool)item));
            if (!item) return;
            Assert.That(state.Position, Is.EqualTo(item.transform.position));
            Assert.That(state.ActiveSelf, Is.EqualTo(item.gameObject.activeSelf));
            Assert.That(state.ActiveInHierarchy, Is.EqualTo(item.gameObject.activeInHierarchy));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlobalMotionAndHierarchyChangesStaySynchronous(bool rect)
        {
            var parent = Item(); var other = Item();
            var item = Item(parent.transform, rect); var target = snapshot.Track(item);
            snapshot.BeginObservation(); Match(target, item);
            foreach (Action mutate in new Action[] {
                () => item.transform.position += Vector3.one,
                () => parent.transform.position += Vector3.right,
                () => parent.transform.rotation = Quaternion.Euler(0, 0, 25),
                () => parent.transform.localScale = new Vector3(2, 3, 1),
                () => parent.gameObject.SetActive(false),
                () => item.transform.position += Vector3.up,
                () => item.transform.SetParent(other.transform, false),
                () => item.gameObject.SetActive(false),
                () => item.gameObject.SetActive(true) })
            {
                mutate(); snapshot.BeginObservation(); Match(target, item);
            }
        }

        [Test]
        public void ComponentAndAncestorDestructionInvalidateLiveHandles()
        {
            var parent = Item(); var first = Item(parent.transform); var second = Item(parent.transform, true);
            var one = snapshot.Track(first); var two = snapshot.Track(second);
            snapshot.BeginObservation(); Match(one, first); Match(two, second);
            Object.DestroyImmediate(first);
            snapshot.BeginObservation(); Match(one, first); Match(two, second);
            Object.DestroyImmediate(parent.gameObject);
            snapshot.BeginObservation(); Match(one, first); Match(two, second);
        }

        [Test]
        public void ExplicitCallbackInvalidationReadsLaterTargetsAgainInTheSameFrame()
        {
            var parent = Item(); var first = Item(parent.transform); var second = Item(parent.transform, true);
            var one = snapshot.Track(first); var two = snapshot.Track(second);
            snapshot.BeginObservation(); Match(one, first); Match(two, second);
            snapshot.Invalidate();
            first.gameObject.SetActive(false);
            second.transform.position += new Vector3(10, 20, 0);
            snapshot.Invalidate();
            Match(two, second); Match(one, first);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActivationOnly_PreservesHierarchyCallbacksLifetimeAndLivePose(bool rect)
        {
            snapshot.Dispose();
            Assert.That(SceneObjectSnapshot2D.TryCreateActivation(out snapshot, out string reason), Is.True, reason);
            Assert.That(snapshot.TracksMotion, Is.False);
            var parent = Item(); var other = Item(); var item = Item(parent.transform, rect);
            var target = snapshot.Track(item);
            foreach (Action mutate in new Action[] {
                () => item.transform.position += Vector3.one,
                () => parent.transform.localScale = new Vector3(2, 3, 1),
                () => parent.gameObject.SetActive(false),
                () => item.transform.SetParent(other.transform, true),
                () => item.gameObject.SetActive(false),
                () => item.gameObject.SetActive(true) })
            {
                mutate(); snapshot.Invalidate(); Match(target, item);
                Assert.That(snapshot.TryReadActivation(target, out var flags), Is.True);
                Assert.That(flags.ActiveSelf, Is.EqualTo(item.gameObject.activeSelf));
                Assert.That(flags.ActiveInHierarchy, Is.EqualTo(item.gameObject.activeInHierarchy));
            }
            Object.DestroyImmediate(item); snapshot.Invalidate();
            Assert.That(snapshot.TryReadActivation(target, out var removed), Is.True);
            Assert.That(removed.Alive, Is.False);
            snapshot.Dispose(); Assert.That(snapshot.TryReadActivation(target, out _), Is.False);
        }

        [Test]
        public void ActivationOnly_OrdinaryMotionDoesNotRebuildActivationState()
        {
            snapshot.Dispose();
            Assert.That(SceneObjectSnapshot2D.TryCreateActivation(out snapshot, out string reason), Is.True, reason);
            var parent = Item(); var item = Item(parent.transform); var target = snapshot.Track(item);
            snapshot.BeginObservation(); Assert.That(snapshot.TryReadActivation(target, out _), Is.True);
            long reads = snapshot.StateReads;
            for (int i = 0; i < 100; i++)
            {
                parent.transform.position += Vector3.one;
                item.transform.position += Vector3.right;
                snapshot.BeginObservation(); Assert.That(snapshot.TryReadActivation(target, out var state), Is.True);
                Assert.That(state.ActiveInHierarchy, Is.True);
            }
            Assert.That(snapshot.StateReads, Is.EqualTo(reads));
            Match(target, item); // A full read from this mode still returns the current pose.
        }

        [Test]
        public void UnchangedTargetsReuseStateAndUntrackedDisposedHandlesFailClosed()
        {
            var item = Item(); var target = snapshot.Track(item);
            snapshot.BeginObservation(); Match(target, item);
            long reads = snapshot.StateReads;
            snapshot.BeginObservation(); Match(target, item);
            Assert.That(snapshot.StateReads, Is.EqualTo(reads));
            Assert.That(snapshot.SnapshotHits, Is.GreaterThan(reads));
            snapshot.Untrack(target);
            Assert.That(snapshot.Count, Is.Zero);
            Assert.That(snapshot.TryRead(target, out _), Is.False);
            var next = snapshot.Track(item);
            snapshot.BeginObservation(); Match(next, item);
            snapshot.Dispose();
            Assert.That(snapshot.IsActive, Is.False);
            Assert.That(snapshot.TryRead(next, out _), Is.False);
            Assert.That(item, Is.Not.Null);
        }
    }
}
