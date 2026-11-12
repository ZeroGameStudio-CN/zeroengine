using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicBindingRegistryTests
    {
        [Test]
        public void RegisterAndResolve_ReturnsBinding()
        {
            var registry = new CinematicBindingRegistry();
            var binding = new GameObject("Binding");

            registry.Register("actor.storyteller", binding);
            var resolved = registry.TryResolve("actor.storyteller", out var result);

            Assert.IsTrue(resolved);
            Assert.AreSame(binding, result);
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void RegisterAndResolve_NormalizesBindingKey()
        {
            var registry = new CinematicBindingRegistry();
            var binding = new GameObject("Binding");

            registry.Register("  actor.storyteller  ", binding);
            var resolved = registry.TryResolve("actor.storyteller", out var result);

            Assert.IsTrue(resolved);
            Assert.AreSame(binding, result);
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void UnregisterMatchingBinding_RemovesBinding()
        {
            var registry = new CinematicBindingRegistry();
            var binding = new GameObject("Binding");
            registry.Register("actor.storyteller", binding);

            var removed = registry.Unregister("actor.storyteller", binding);

            Assert.IsTrue(removed);
            Assert.IsFalse(registry.TryResolve("actor.storyteller", out _));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Validate_DuplicateBindingKey_ReportsDuplicate()
        {
            var registry = new CinematicBindingRegistry();
            var first = new GameObject("First");
            var second = new GameObject("Second");
            registry.Register("actor.storyteller", first);
            registry.Register("actor.storyteller", second);

            var issues = registry.Validate();

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateBindingKey &&
                    issue.ContextId == "actor.storyteller"));
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        [Test]
        public void Validate_InvalidBindingKey_ReportsInvalidStableId()
        {
            var registry = new CinematicBindingRegistry();
            var binding = new GameObject("Binding");
            registry.Register("actor/Storyteller", binding);

            var issues = registry.Validate();

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.InvalidStableId &&
                    issue.ContextId == "actor/Storyteller"));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void BindingSource_OnEnable_RegistersGameObjectWithRegistryBehaviour()
        {
            var registryObject = new GameObject("Registry");
            var registry = registryObject.AddComponent<CinematicBindingRegistryBehaviour>();
            var sourceObject = new GameObject("Source");
            var source = sourceObject.AddComponent<CinematicBindingSource>();
            SetObject(source, "_registry", registry);
            SetString(source, "_bindingKey", "actor.storyteller");

            InvokeLifecycle(source, "OnEnable");

            Assert.IsTrue(registry.TryResolve("actor.storyteller", out var binding));
            Assert.AreSame(sourceObject, binding);

            InvokeLifecycle(source, "OnDisable");

            Assert.IsFalse(registry.TryResolve("actor.storyteller", out _));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(registryObject);
        }

        [Test]
        public void BindingSource_ExplicitBindingObject_RegistersThatObject()
        {
            var registryObject = new GameObject("Registry");
            var registry = registryObject.AddComponent<CinematicBindingRegistryBehaviour>();
            var sourceObject = new GameObject("Source");
            var bindingObject = new GameObject("Binding");
            var source = sourceObject.AddComponent<CinematicBindingSource>();
            SetObject(source, "_registry", registry);
            SetObject(source, "_binding", bindingObject);
            SetString(source, "_bindingKey", "actor.storyteller");

            InvokeLifecycle(source, "OnEnable");

            Assert.IsTrue(registry.TryResolve("actor.storyteller", out var binding));
            Assert.AreSame(bindingObject, binding);

            InvokeLifecycle(source, "OnDisable");

            Assert.IsFalse(registry.TryResolve("actor.storyteller", out _));
            Object.DestroyImmediate(bindingObject);
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(registryObject);
        }

        [Test]
        public void BindingSource_WithoutExplicitRegistry_FindsRegistryBehaviourInScene()
        {
            var registryObject = new GameObject("Registry");
            var registry = registryObject.AddComponent<CinematicBindingRegistryBehaviour>();
            var sourceObject = new GameObject("Source");
            var source = sourceObject.AddComponent<CinematicBindingSource>();
            SetString(source, "_bindingKey", "actor.storyteller");

            InvokeLifecycle(source, "OnEnable");

            Assert.IsTrue(registry.TryResolve("actor.storyteller", out var binding));
            Assert.AreSame(sourceObject, binding);
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(registryObject);
        }

        [Test]
        public void BindingSource_SetRegistry_RebindsActiveSourceToExplicitRegistry()
        {
            var firstRegistryObject = new GameObject("First Registry");
            var firstRegistry = firstRegistryObject.AddComponent<CinematicBindingRegistryBehaviour>();
            var secondRegistryObject = new GameObject("Second Registry");
            var secondRegistry = secondRegistryObject.AddComponent<CinematicBindingRegistryBehaviour>();
            var sourceObject = new GameObject("Source");
            var source = sourceObject.AddComponent<CinematicBindingSource>();
            SetObject(source, "_registry", firstRegistry);
            SetString(source, "_bindingKey", "actor.storyteller");

            InvokeLifecycle(source, "OnEnable");
            source.SetRegistry(secondRegistry);

            Assert.IsFalse(firstRegistry.TryResolve("actor.storyteller", out _));
            Assert.IsTrue(secondRegistry.TryResolve("actor.storyteller", out var binding));
            Assert.AreSame(sourceObject, binding);

            InvokeLifecycle(source, "OnDisable");

            Assert.IsFalse(secondRegistry.TryResolve("actor.storyteller", out _));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(secondRegistryObject);
            Object.DestroyImmediate(firstRegistryObject);
        }

        [Test]
        public void BindingSource_SetRegistry_SameActiveRegistry_DoesNotCreateDuplicateBinding()
        {
            var registryObject = new GameObject("Registry");
            var registry = registryObject.AddComponent<CinematicBindingRegistryBehaviour>();
            var sourceObject = new GameObject("Source");
            var source = sourceObject.AddComponent<CinematicBindingSource>();
            SetObject(source, "_registry", registry);
            SetString(source, "_bindingKey", "actor.storyteller");

            InvokeLifecycle(source, "OnEnable");
            source.SetRegistry(registry);

            Assert.That(registry.Validate(), Has.None
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateBindingKey));

            InvokeLifecycle(source, "OnDisable");

            Assert.IsFalse(registry.TryResolve("actor.storyteller", out _));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(registryObject);
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void InvokeLifecycle(CinematicBindingSource source, string methodName)
        {
            typeof(CinematicBindingSource)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(source, null);
        }
    }
}
