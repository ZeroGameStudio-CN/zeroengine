using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class SpritePoseHistoryTests
    {
        private GameObject root;
        private SpriteRenderer source;
        private SpriteRenderer[] targets;
        private Texture2D texture;
        private Sprite first, second;
        private SpritePoseHistory history;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SyntheticPoseHistory");
            source = Child("Source");
            texture = new Texture2D(2, 1);
            first = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
            second = Sprite.Create(texture, new Rect(1, 0, 1, 1), Vector2.one * 0.5f);
            source.sprite = first;
            targets = new[] { Child("Echo0"), Child("Echo1"), Child("Echo2") };
            history = new SpritePoseHistory(targets);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void CaptureFreezesFramePoseFlipsColorAndSortingAfterSourceReuseOrDestruction()
        {
            source.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(10, 20, 30));
            source.transform.localScale = new Vector3(2, 3, 1);
            source.flipX = source.flipY = true;
            source.sortingOrder = 20;
            source.color = new Color(0.4f, 0.5f, 0.6f, 0.8f);
            var rotation = source.transform.rotation;
            Assert.That(history.TryCapture(source, 0.5f, new Color(1, 0.5f, 1, 0.5f), sortingOrderOffset: -1), Is.True);
            source.sprite = second;
            source.transform.position = Vector3.one * 200;
            source.transform.localScale = Vector3.one;
            source.flipX = source.flipY = false;
            source.color = Color.white;
            history.Advance(0.125f);
            Assert.That(targets[0].sprite, Is.SameAs(first));
            Assert.That(targets[0].transform.position, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(Quaternion.Angle(targets[0].transform.rotation, rotation), Is.LessThan(0.001f));
            Assert.That((targets[0].transform.lossyScale - new Vector3(2, 3, 1)).sqrMagnitude, Is.LessThan(0.000001f));
            Assert.That(targets[0].flipX && targets[0].flipY, Is.True);
            Assert.That(targets[0].sortingOrder, Is.EqualTo(19));
            Assert.That(targets[0].color.a, Is.EqualTo(0.3f).Within(0.00001f));
            Assert.That(targets[0].color.g, Is.EqualTo(0.25f));
            Object.DestroyImmediate(source.gameObject);
            Assert.That(history.Advance(0.125f), Is.True);
            Assert.That(targets[0].sprite, Is.SameAs(first));
            Assert.That(history.Advance(0.25f), Is.False);
            Assert.That(targets[0].enabled, Is.False);
            Assert.That(targets[0].sprite, Is.Null);
        }

        [Test]
        public void PauseAndInvalidDeltaFreezeAgeWhileIntensityDoesNotChangeExpiry()
        {
            history.TryCapture(source, 0.5f, Color.white);
            foreach (float delta in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.That(history.Advance(delta), Is.True);
                Assert.That(targets[0].color.a, Is.EqualTo(1f));
            }
            Assert.That(history.Advance(0.125f, 0f), Is.True);
            Assert.That(targets[0].enabled, Is.False);
            history.Advance(0f, 0.5f);
            Assert.That(targets[0].color.a, Is.EqualTo(0.375f).Within(0.00001f));
            Assert.That(history.Advance(0.375f), Is.False);
        }

        [Test]
        public void CapacityRejectsOrReplacesOldestOnlyWhenExplicit()
        {
            for (int i = 0; i < 3; i++)
            {
                source.transform.position = Vector3.right * (i + 1);
                Assert.That(history.TryCapture(source, 1f, Color.white), Is.True);
                history.Advance(0.125f);
            }
            source.transform.position = Vector3.right * 9;
            Assert.That(history.TryCapture(source, 1f, Color.white), Is.False);
            Assert.That(targets[0].transform.position.x, Is.EqualTo(1f));
            Assert.That(history.TryCapture(source, 1f, Color.white, replaceOldest: true), Is.True);
            Assert.That(targets[0].transform.position.x, Is.EqualTo(9f));
            Assert.That(history.ActiveCount, Is.EqualTo(3));
            Assert.That(history.Capacity, Is.EqualTo(3));
            history.Clear();
            Assert.That(history.Advance(0f), Is.False);
            foreach (var target in targets)
            {
                Assert.That(target.enabled, Is.False);
                Assert.That(target.sprite, Is.Null);
            }
            Assert.That(history.TryCapture(source, 0.5f, Color.white), Is.True);
            Assert.That(targets[0].enabled, Is.True);
        }

        [Test]
        public void InvalidCaptureDoesNotReplaceExistingPlayback()
        {
            history.TryCapture(source, 0.5f, Color.white);
            Assert.Throws<ArgumentException>(() => new SpritePoseHistory(Array.Empty<SpriteRenderer>()));
            Assert.Throws<ArgumentException>(() => new SpritePoseHistory(new[] { targets[0], targets[0] }));
            Assert.That(history.TryCapture(null, 0.5f, Color.white), Is.False);
            Assert.That(history.TryCapture(targets[0], 0.5f, Color.white), Is.False);
            foreach (float duration in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                Assert.That(history.TryCapture(source, duration, Color.white), Is.False);
            Assert.That(history.TryCapture(source, 0.5f, new Color(float.NaN, 1, 1)), Is.False);
            Assert.That(history.TryCapture(source, 0.5f, Color.white, worldPosition: Vector3.one * float.PositiveInfinity), Is.False);
            Assert.That(history.ActiveCount, Is.EqualTo(1));
            Assert.That(targets[0].enabled, Is.True);
        }

        [Test]
        public void EachCaptureOwnsItsLifetimeAndClearDoesNotDestroyBorrowedObjects()
        {
            history.TryCapture(source, 0.125f, Color.white, worldPosition: new Vector3(4, 5, 6));
            history.TryCapture(source, 0.5f, Color.white);
            Assert.That(targets[0].transform.position, Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(history.Advance(0.125f), Is.True);
            Assert.That(history.ActiveCount, Is.EqualTo(1));
            Assert.That(targets[0].sprite, Is.Null);
            Object.DestroyImmediate(targets[1].gameObject);
            Assert.That(history.Advance(0f), Is.False);
            history.Clear();
            Assert.That(targets[0] && source, Is.True);
        }

        [Test]
        public void WarmedCaptureAndAdvanceDoNotAllocateManagedMemory()
        {
            for (int i = 0; i < 20; i++) { history.TryCapture(source, 0.2f, Color.white, true); history.Advance(0.01f); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { history.TryCapture(source, 0.2f, Color.white, true); history.Advance(0.01f); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private SpriteRenderer Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform);
            return child.AddComponent<SpriteRenderer>();
        }
    }
}
