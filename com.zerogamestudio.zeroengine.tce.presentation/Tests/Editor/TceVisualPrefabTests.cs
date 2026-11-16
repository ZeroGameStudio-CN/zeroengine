using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.TCE.Presentation.Tests
{
    public sealed class TceVisualPrefabTests
    {
        private GameObject source, copy;
        private Mesh mesh;

        [TearDown]
        public void Cleanup()
        {
            TceVisualPrefab.Destroy(copy);
            TceVisualPrefab.Destroy(source);
            if (mesh) Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MixedGraphRetainsMeshesSpritesParticlesAndAuthoredChildPoses()
        {
            source = new GameObject("Authored visual");
            source.SetActive(false);
            mesh = new Mesh();
            source.AddComponent<MeshFilter>().sharedMesh = mesh;
            source.AddComponent<MeshRenderer>();
            var child = new GameObject("Mixed child");
            child.transform.SetParent(source.transform, false);
            child.transform.localPosition = new Vector3(3, 4, 5);
            child.AddComponent<SpriteRenderer>();
            child.AddComponent<ParticleSystem>();
            child.AddComponent<Light>();
            Assert.That(TceVisualPrefab.TryCreate(source, null, out var recipe, out var reason), Is.True, reason);
            copy = recipe.Instantiate();
            Assert.That(copy.activeSelf, Is.False);
            Assert.That(copy.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(copy.transform.GetChild(0).localPosition, Is.EqualTo(new Vector3(3, 4, 5)));
            Assert.That(copy.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(3));
            Assert.That(copy.GetComponentInChildren<Light>(true), Is.Not.Null);
        }

        [TestCase(typeof(BoxCollider))]
        public void UnadmittedComponentsAreRejectedBeforeCreatingAnyCopy(System.Type component)
        {
            source = new GameObject("Rejected source");
            source.AddComponent<SpriteRenderer>();
            source.AddComponent(component);
            Assert.That(TceVisualPrefab.TryCreate(source, null, out var recipe, out var reason), Is.False);
            Assert.That(recipe, Is.Null);
            Assert.That(reason, Does.StartWith("unsupported-component:"));
        }

        [Test]
        public void ReadOnlyMeshesNeedNotBeReadableOrBaked()
        {
            source = new GameObject("Read-only mesh");
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.UploadMeshData(true);
            source.AddComponent<MeshFilter>().sharedMesh = mesh;
            source.AddComponent<MeshRenderer>();
            Assert.That(TceVisualPrefab.TryCreate(source, null, out var recipe, out _), Is.True);
            copy = recipe.Instantiate();
            Assert.That(copy.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(mesh.isReadable, Is.False);
        }

        [Test]
        public void SourceSuppressionAndParticleStopCallbacksAreNotInheritedByTheDisplay()
        {
            source = new GameObject("Suppressed particle");
            source.SetActive(false);
            var ps = source.AddComponent<ParticleSystem>();
            var main = ps.main; main.stopAction = ParticleSystemStopAction.Callback;
            source.GetComponent<ParticleSystemRenderer>().forceRenderingOff = true;
            Assert.That(TceVisualPrefab.TryCreate(source, null, out var recipe, out _), Is.True);
            copy = recipe.Instantiate();
            Assert.That(copy.GetComponent<ParticleSystemRenderer>().forceRenderingOff, Is.False);
            Assert.That(copy.GetComponent<ParticleSystem>().main.stopAction, Is.EqualTo(ParticleSystemStopAction.None));
            Assert.That(ps.main.stopAction, Is.EqualTo(ParticleSystemStopAction.Callback));
            Assert.That(source.GetComponent<ParticleSystemRenderer>().forceRenderingOff, Is.True);
        }

        [Test]
        public void ANewGameplayComponentInvalidatesAnAlreadyAdmittedRecipe()
        {
            source = new GameObject("Mutable template");
            source.AddComponent<SpriteRenderer>();
            Assert.That(TceVisualPrefab.TryCreate(source, null, out var recipe, out _), Is.True);
            source.AddComponent<BoxCollider>();
            Assert.Throws<System.InvalidOperationException>(() => recipe.Instantiate());
        }

        [Test]
        public void SourceOnlyBehavioursNeverAwakeOnTheDisplay()
        {
            source = new GameObject("Source-only lifetime");
            source.SetActive(false);
            source.AddComponent<SpriteRenderer>();
            source.AddComponent<VisualLifetimeProbe>();
            VisualLifetimeProbe.Awakes = 0;
            Assert.That(TceVisualPrefab.TryCreate(source, new TceVisualProjectionPolicy { SourceOnly = b => b is VisualLifetimeProbe }, out var recipe, out _), Is.True);
            copy = recipe.Instantiate(); copy.SetActive(true);
            Assert.That(copy.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(VisualLifetimeProbe.Awakes, Is.Zero);
            Assert.That(source.GetComponent<VisualLifetimeProbe>(), Is.Not.Null);
        }
    }

    public sealed class VisualLifetimeProbe : MonoBehaviour
    {
        public static int Awakes;
        private void Awake() { Awakes++; }
    }
}
