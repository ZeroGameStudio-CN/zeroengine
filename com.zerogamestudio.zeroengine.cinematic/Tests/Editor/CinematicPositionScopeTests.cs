using System;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPositionScopeTests
    {
        private Scene _scene;
        private Transform _subject;
        private readonly Vector3 _home = new Vector3(1f, 0f, 2f);

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _subject = Create("Subject");
            _subject.position = _home;
        }

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void OwnedScope_RestoresOnlyItsPositionAndIsIdempotent()
        {
            var scope = new CinematicPositionScope(_subject, _home, () => true);
            Assert.That(scope.TrySetPosition(_home + Vector3.right), Is.True);
            var rotation = Quaternion.Euler(0f, 45f, 0f);
            _subject.rotation = rotation;
            _subject.localScale = Vector3.one * 2f;
            scope.Dispose();
            Assert.That(_subject.position, Is.EqualTo(_home));
            Assert.That(_subject.rotation, Is.EqualTo(rotation));
            Assert.That(_subject.localScale, Is.EqualTo(Vector3.one * 2f));
            _subject.position = Vector3.one * 20f;
            scope.Dispose();
            Assert.That(_subject.position, Is.EqualTo(Vector3.one * 20f));
        }

        [Test]
        public void ExternalPlacement_RetiresEvenIfThePositionLaterMatches()
        {
            using var scope = new CinematicPositionScope(_subject, _home, () => true);
            Vector3 last = _home + Vector3.forward;
            scope.TrySetPosition(last);
            _subject.position = Vector3.one * 20f;
            Assert.That(scope.HasOwnership, Is.False);
            _subject.position = last;
            Assert.That(scope.TryRestore(), Is.False);
            Assert.That(_subject.position, Is.EqualTo(last));
        }

        [Test]
        public void NewOwnerAtTheSamePosition_IsNotRestoredByTheOldScope()
        {
            int owner = 1;
            var old = new CinematicPositionScope(_subject, _home, () => owner == 1);
            Vector3 replacementHome = _home + Vector3.right;
            old.TrySetPosition(replacementHome);
            owner = 2;
            using var current = new CinematicPositionScope(_subject, replacementHome, () => owner == 2);
            old.Dispose();
            Assert.That(_subject.position, Is.EqualTo(replacementHome));
            Assert.That(current.TrySetPosition(Vector3.one * 3f), Is.True);
        }

        [Test]
        public void ParentChangeAtTheSameWorldPosition_EndsOwnership()
        {
            using var scope = new CinematicPositionScope(_subject, _home, () => true);
            var last = _home + Vector3.forward;
            scope.TrySetPosition(last);
            _subject.SetParent(Create("NewOwner"), true);
            Assert.That(scope.TryRestore(), Is.False);
            Assert.That(_subject.position, Is.EqualTo(last));
        }

        [Test]
        public void ObservedInactiveSubject_DoesNotResumeAfterReenable()
        {
            using var scope = new CinematicPositionScope(_subject, _home, () => true);
            scope.TrySetPosition(_home + Vector3.right);
            _subject.gameObject.SetActive(false);
            Assert.That(scope.HasOwnership, Is.False);
            _subject.gameObject.SetActive(true);
            Assert.That(scope.TryRestore(), Is.False);
        }

        [Test]
        public void DestroyedSubject_CanBeReleasedWithoutFault()
        {
            var scope = new CinematicPositionScope(_subject, _home, () => true);
            Object.DestroyImmediate(_subject.gameObject);
            Assert.DoesNotThrow(scope.Dispose);
            Assert.That(scope.HasOwnership, Is.False);
        }

        [Test]
        public void ExplicitRestore_AllowsFurtherSamplingUntilFinalDisposal()
        {
            using var scope = new CinematicPositionScope(_subject, _home, () => true);
            Assert.That(scope.TrySetPosition(_home + Vector3.right), Is.True);
            Assert.That(scope.TryRestore(), Is.True);
            Assert.That(scope.TryGetPosition(out var position), Is.True);
            Assert.That(position, Is.EqualTo(_home));
            Assert.That(scope.TrySetPosition(_home + Vector3.forward), Is.True);
        }

        [Test]
        public void InvalidData_DoesNotWriteOrRetireAValidScope()
        {
            Assert.Throws<ArgumentNullException>(() => new CinematicPositionScope(null, _home, () => true));
            Assert.Throws<ArgumentNullException>(() => new CinematicPositionScope(_subject, _home, null));
            Assert.Throws<ArgumentException>(() => new CinematicPositionScope(_subject, new Vector3(float.NaN, 0f, 0f), () => true));
            using var scope = new CinematicPositionScope(_subject, _home, () => true);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentException>(() => scope.TrySetPosition(new Vector3(0f, invalid, 0f)));
            Assert.That(_subject.position, Is.EqualTo(_home));
            Assert.That(scope.HasOwnership, Is.True);
        }

        [Test]
        public void FailingOwnerPredicate_RetiresBeforeTheExceptionEscapes()
        {
            int observations = 0;
            var scope = new CinematicPositionScope(_subject, _home, () =>
            {
                observations++;
                throw new InvalidOperationException("Owner retired.");
            });
            Assert.Throws<InvalidOperationException>(() => scope.TryRestore());
            Assert.DoesNotThrow(scope.Dispose);
            Assert.That(observations, Is.EqualTo(1));
        }

        [Test]
        public void RevokedOwner_CannotRegainTheSameScope()
        {
            bool current = true;
            using var scope = new CinematicPositionScope(_subject, _home, () => current);
            scope.TrySetPosition(_home + Vector3.right);
            current = false;
            Assert.That(scope.HasOwnership, Is.False);
            current = true;
            Assert.That(scope.TryRestore(), Is.False);
        }

        private Transform Create(string name)
        {
            var item = new GameObject(name);
            SceneManager.MoveGameObjectToScene(item, _scene);
            return item.transform;
        }
    }
}
