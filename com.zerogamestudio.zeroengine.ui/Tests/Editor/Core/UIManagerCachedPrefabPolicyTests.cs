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
    public sealed class UIManagerCachedPrefabPolicyTests
    {
        [Test]
        public void Resolve_AllInputsTrue_UsesCachedPrefab()
        {
            var decision = UIManagerCachedPrefabPolicy.Resolve(
                hasHandle: true,
                handleIsValid: true,
                loadSucceeded: true,
                hasPrefabResult: true);

            Assert.That(decision.UseCachedPrefab, Is.True);
        }

        [Test]
        public void Resolve_MissingHandle_DoesNotUseCachedPrefab()
        {
            var decision = UIManagerCachedPrefabPolicy.Resolve(
                hasHandle: false,
                handleIsValid: true,
                loadSucceeded: true,
                hasPrefabResult: true);

            Assert.That(decision.UseCachedPrefab, Is.False);
        }

        [Test]
        public void Resolve_InvalidHandle_DoesNotUseCachedPrefab()
        {
            var decision = UIManagerCachedPrefabPolicy.Resolve(
                hasHandle: true,
                handleIsValid: false,
                loadSucceeded: true,
                hasPrefabResult: true);

            Assert.That(decision.UseCachedPrefab, Is.False);
        }

        [Test]
        public void Resolve_LoadNotSucceeded_DoesNotUseCachedPrefab()
        {
            var decision = UIManagerCachedPrefabPolicy.Resolve(
                hasHandle: true,
                handleIsValid: true,
                loadSucceeded: false,
                hasPrefabResult: true);

            Assert.That(decision.UseCachedPrefab, Is.False);
        }

        [Test]
        public void Resolve_MissingPrefabResult_DoesNotUseCachedPrefab()
        {
            var decision = UIManagerCachedPrefabPolicy.Resolve(
                hasHandle: true,
                handleIsValid: true,
                loadSucceeded: true,
                hasPrefabResult: false);

            Assert.That(decision.UseCachedPrefab, Is.False);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_TryGetCachedPrefab_DelegatesCacheEligibilityToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerCachedPrefabPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager cached prefab eligibility must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var tryGetCachedPrefab = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private bool TryGetCachedPrefab(string handleKey, out GameObject prefab)");

            Assert.That(tryGetCachedPrefab, Does.Contain("_prefabHandles.TryGetValue(handleKey, out var cachedHandle)"));
            Assert.That(tryGetCachedPrefab, Does.Contain("var handleIsValid = hasHandle && cachedHandle.IsValid();"));
            Assert.That(tryGetCachedPrefab, Does.Contain("var loadSucceeded = handleIsValid && cachedHandle.Status == AsyncOperationStatus.Succeeded;"));
            Assert.That(tryGetCachedPrefab, Does.Contain("var cachedPrefab = loadSucceeded ? cachedHandle.Result : null;"));
            Assert.That(tryGetCachedPrefab, Does.Contain("UIManagerCachedPrefabPolicy.Resolve("));
            Assert.That(tryGetCachedPrefab, Does.Contain("hasHandle: hasHandle"));
            Assert.That(tryGetCachedPrefab, Does.Contain("handleIsValid: handleIsValid"));
            Assert.That(tryGetCachedPrefab, Does.Contain("loadSucceeded: loadSucceeded"));
            Assert.That(tryGetCachedPrefab, Does.Contain("hasPrefabResult: cachedPrefab != null"));
            Assert.That(tryGetCachedPrefab, Does.Contain("prefab = cachedPrefab;"));
            Assert.That(tryGetCachedPrefab, Does.Not.Contain("|| !cachedHandle.IsValid()"));
            Assert.That(tryGetCachedPrefab, Does.Not.Contain("cachedHandle.Status != AsyncOperationStatus.Succeeded"));
            AssertOrder(
                tryGetCachedPrefab,
                "_prefabHandles.TryGetValue(handleKey, out var cachedHandle)",
                "var handleIsValid = hasHandle && cachedHandle.IsValid();",
                "The cache handle must be looked up before validity is checked.");
            AssertOrder(
                tryGetCachedPrefab,
                "var handleIsValid = hasHandle && cachedHandle.IsValid();",
                "var loadSucceeded = handleIsValid && cachedHandle.Status == AsyncOperationStatus.Succeeded;",
                "Cached handle status must only be checked after the handle is valid.");
            AssertOrder(
                tryGetCachedPrefab,
                "var loadSucceeded = handleIsValid && cachedHandle.Status == AsyncOperationStatus.Succeeded;",
                "var cachedPrefab = loadSucceeded ? cachedHandle.Result : null;",
                "Cached handle result must only be read after the load succeeded.");
            AssertOrder(
                tryGetCachedPrefab,
                "UIManagerCachedPrefabPolicy.Resolve(",
                "prefab = cachedPrefab;",
                "The out prefab should only be assigned after the policy decision.");

            Assert.That(policySource, Does.Contain("public static UIManagerCachedPrefabDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationHandle"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("_prefabHandles"));
            Assert.That(policySource, Does.Not.Contain("cachedHandle"));
            Assert.That(policySource, Does.Not.Contain(".Result"));
            Assert.That(policySource, Does.Not.Contain(".IsValid("));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationStatus"));
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


