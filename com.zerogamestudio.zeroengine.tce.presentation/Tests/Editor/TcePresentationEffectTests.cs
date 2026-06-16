using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.TCE;
using ZeroEngine.TCE.Presentation;

namespace ZeroEngine.TCE.Presentation.Tests.Editor
{
    [TestFixture]
    public sealed class TcePresentationEffectTests
    {
        [Test]
        public void SpawnSnapshotEffectData_DeclaresDocumentationMetadata()
        {
            var doc = typeof(SpawnSnapshotEffectData)
                .GetCustomAttributes(typeof(TceComponentDocAttribute), false)
                .Cast<TceComponentDocAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(doc);
            Assert.AreEqual(TceComponentDocCategory.Effect, doc.Category);
            Assert.AreEqual("zeroengine.tce.presentation.effect.spawn_snapshot", doc.ComponentId);
        }

        [Test]
        public void SpawnSoulGhostEffectData_DeclaresDocumentationMetadata()
        {
            var doc = typeof(SpawnSoulGhostEffectData)
                .GetCustomAttributes(typeof(TceComponentDocAttribute), false)
                .Cast<TceComponentDocAttribute>()
                .SingleOrDefault();

            Assert.IsNotNull(doc);
            Assert.AreEqual(TceComponentDocCategory.Effect, doc.Category);
            Assert.AreEqual("zeroengine.tce.presentation.effect.spawn_soul_ghost", doc.ComponentId);
            Assert.AreEqual(TcePresentationStyle.SoulGhost, new SpawnSoulGhostEffectData().Settings.Style);
        }

        [Test]
        public void PresentationEffectData_SettingsFieldHasDocumentation()
        {
            Assert.IsNotEmpty(typeof(SpawnSnapshotEffectData)
                .GetField(nameof(SpawnSnapshotEffectData.Settings))
                ?.GetCustomAttributes(typeof(TceFieldDocAttribute), true));

            Assert.IsNotEmpty(typeof(SpawnSoulGhostEffectData)
                .GetField(nameof(SpawnSoulGhostEffectData.Settings))
                ?.GetCustomAttributes(typeof(TceFieldDocAttribute), true));
        }

        [Test]
        public void Execute_WithoutPresentationSource_DoesNotThrow()
        {
            var actor = new TestActor();
            var effect = new SpawnSnapshotEffect();
            effect.Initialize(
                new TceComponentContext(new TceRuntime(), new TceGraph(), actor, null, new ManualClock()),
                new SpawnSnapshotEffectData());

            Assert.DoesNotThrow(() => effect.Execute(actor, null));
        }

        [Test]
        public void Execute_TargetActorPresentationSource_UsesAdapterSource()
        {
            var actor = new TestPresentationActor();
            var effect = new SpawnSnapshotEffect();
            effect.Initialize(
                new TceComponentContext(new TceRuntime(), new TceGraph(), actor, null, new ManualClock()),
                new SpawnSnapshotEffectData());

            effect.Execute(actor, null);

            Assert.AreEqual(1, actor.CaptureCount);
        }

        [Test]
        public void Execute_NativePresentationSource_UsesAdapterSource()
        {
            var source = new TestPresentationSource();
            var actor = new TestActor(source);
            var effect = new SpawnSnapshotEffect();
            effect.Initialize(
                new TceComponentContext(new TceRuntime(), new TceGraph(), actor, null, new ManualClock()),
                new SpawnSnapshotEffectData());

            effect.Execute(actor, null);

            Assert.AreEqual(1, source.CaptureCount);
        }

        private sealed class TestActor : ITceActor
        {
            private readonly object nativeObject;

            public TestActor(object nativeObject = null)
            {
                this.nativeObject = nativeObject ?? this;
            }

            public bool IsAlive => true;
            public float DomainTime => 0f;
            public object NativeObject => nativeObject;
        }

        private sealed class TestPresentationActor : ITceActor, ITcePresentationSource
        {
            public int CaptureCount { get; private set; }

            public bool IsAlive => true;
            public float DomainTime => 0f;
            public object NativeObject => this;

            public bool TryCaptureSnapshot(TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot)
            {
                CaptureCount++;
                snapshot = null;
                return false;
            }
        }

        private sealed class TestPresentationSource : ITcePresentationSource
        {
            public int CaptureCount { get; private set; }

            public bool TryCaptureSnapshot(TceVisualSnapshotRequest request, out TceVisualSnapshot snapshot)
            {
                CaptureCount++;
                snapshot = null;
                return false;
            }
        }

        private sealed class ManualClock : ITceClock
        {
            public float Now => 0f;
        }
    }
}
