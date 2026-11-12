using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("UI")]
    public sealed class UIManagerPrefabReferencePolicyTests
    {
        [Test]
        public void Resolve_ExistingReferenceWithValidRuntimeKey_LoadsPrefab()
        {
            var decision = UIManagerPrefabReferencePolicy.Resolve(
                hasPrefabReference: true,
                runtimeKeyIsValid: true);

            Assert.That(decision.LoadPrefab, Is.True);
        }

        [Test]
        public void Resolve_MissingReference_DoesNotLoadPrefab()
        {
            var decision = UIManagerPrefabReferencePolicy.Resolve(
                hasPrefabReference: false,
                runtimeKeyIsValid: true);

            Assert.That(decision.LoadPrefab, Is.False);
        }

        [Test]
        public void Resolve_InvalidRuntimeKey_DoesNotLoadPrefab()
        {
            var decision = UIManagerPrefabReferencePolicy.Resolve(
                hasPrefabReference: true,
                runtimeKeyIsValid: false);

            Assert.That(decision.LoadPrefab, Is.False);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_PrefabReferenceGuards_DelegateEligibilityToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerPrefabReferencePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager prefab reference eligibility must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var getOrCreateViewAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<UIViewBase> GetOrCreateViewAsync(");
            var loadViewPrefabAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<GameObject> LoadViewPrefabAsync(");

            Assert.That(getOrCreateViewAsync, Does.Contain("var hasPrefabReference = config.prefabReference != null;"));
            Assert.That(getOrCreateViewAsync, Does.Contain("var runtimeKeyIsValid = hasPrefabReference && config.prefabReference.RuntimeKeyIsValid();"));
            Assert.That(getOrCreateViewAsync, Does.Contain("UIManagerPrefabReferencePolicy.Resolve("));
            Assert.That(getOrCreateViewAsync, Does.Contain("hasPrefabReference: hasPrefabReference"));
            Assert.That(getOrCreateViewAsync, Does.Contain("runtimeKeyIsValid: runtimeKeyIsValid"));
            Assert.That(getOrCreateViewAsync, Does.Contain("if (prefabReferenceDecision.LoadPrefab)"));
            Assert.That(getOrCreateViewAsync, Does.Contain("_viewHandleKeys[viewName] = config.prefabReference.RuntimeKey.ToString();"));
            Assert.That(getOrCreateViewAsync, Does.Contain("prefab = await LoadViewPrefabAsync(config.prefabReference);"));
            Assert.That(getOrCreateViewAsync, Does.Not.Contain("if (config.prefabReference != null && config.prefabReference.RuntimeKeyIsValid())"));
            AssertOrder(
                getOrCreateViewAsync,
                "var hasPrefabReference = config.prefabReference != null;",
                "var runtimeKeyIsValid = hasPrefabReference && config.prefabReference.RuntimeKeyIsValid();",
                "The prefab reference must be checked before RuntimeKeyIsValid.");
            AssertOrder(
                getOrCreateViewAsync,
                "UIManagerPrefabReferencePolicy.Resolve(",
                "_viewHandleKeys[viewName] = config.prefabReference.RuntimeKey.ToString();",
                "The view handle key must only be written after the policy decision.");
            AssertOrder(
                getOrCreateViewAsync,
                "UIManagerPrefabReferencePolicy.Resolve(",
                "prefab = await LoadViewPrefabAsync(config.prefabReference);",
                "Prefab loading must only start after the policy decision.");

            Assert.That(loadViewPrefabAsync, Does.Contain("var hasPrefabReference = prefabRef != null;"));
            Assert.That(loadViewPrefabAsync, Does.Contain("var runtimeKeyIsValid = hasPrefabReference && prefabRef.RuntimeKeyIsValid();"));
            Assert.That(loadViewPrefabAsync, Does.Contain("UIManagerPrefabReferencePolicy.Resolve("));
            Assert.That(loadViewPrefabAsync, Does.Contain("hasPrefabReference: hasPrefabReference"));
            Assert.That(loadViewPrefabAsync, Does.Contain("runtimeKeyIsValid: runtimeKeyIsValid"));
            Assert.That(loadViewPrefabAsync, Does.Contain("if (!prefabReferenceDecision.LoadPrefab)"));
            Assert.That(loadViewPrefabAsync, Does.Contain("return null;"));
            Assert.That(loadViewPrefabAsync, Does.Contain("var handleKey = prefabRef.RuntimeKey.ToString();"));
            Assert.That(loadViewPrefabAsync, Does.Not.Contain("if (prefabRef == null || !prefabRef.RuntimeKeyIsValid())"));
            AssertOrder(
                loadViewPrefabAsync,
                "var hasPrefabReference = prefabRef != null;",
                "var runtimeKeyIsValid = hasPrefabReference && prefabRef.RuntimeKeyIsValid();",
                "The prefab reference must be checked before RuntimeKeyIsValid.");
            AssertOrder(
                loadViewPrefabAsync,
                "UIManagerPrefabReferencePolicy.Resolve(",
                "var handleKey = prefabRef.RuntimeKey.ToString();",
                "RuntimeKey must only be read after the policy decision.");

            Assert.That(policySource, Does.Contain("public static UIManagerPrefabReferenceDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AssetReference"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("_viewHandleKeys"));
            Assert.That(policySource, Does.Not.Contain("LoadViewPrefabAsync"));
            Assert.That(policySource, Does.Not.Contain("RuntimeKey"));
            Assert.That(policySource, Does.Not.Contain("RuntimeKeyIsValid"));
        }

        private static void AssertOrder(string source, string first, string second, string message)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"Missing first marker: {first}");
            Assert.That(secondIndex, Is.GreaterThanOrEqualTo(0), $"Missing second marker: {second}");
            Assert.That(firstIndex, Is.LessThan(secondIndex), message);
        }

        private static string AssetPath(params string[] parts)
        {
            var path = Path.Combine(parts);
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "com.zerogamestudio.zeroengine.ui", path);
        }
    }
}


