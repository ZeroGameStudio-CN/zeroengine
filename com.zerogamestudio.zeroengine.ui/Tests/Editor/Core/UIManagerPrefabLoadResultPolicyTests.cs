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
    public sealed class UIManagerPrefabLoadResultPolicyTests
    {
        [Test]
        public void Resolve_LoadSucceeded_UsesLoadedPrefabAndMarksSuccess()
        {
            var decision = UIManagerPrefabLoadResultPolicy.Resolve(loadSucceeded: true);

            Assert.That(decision.CacheLoadedHandle, Is.True);
            Assert.That(decision.MarkLoadSucceeded, Is.True);
            Assert.That(decision.UseLoadedPrefab, Is.True);
        }

        [Test]
        public void Resolve_LoadNotSucceeded_DoesNotUseLoadedPrefab()
        {
            var decision = UIManagerPrefabLoadResultPolicy.Resolve(loadSucceeded: false);

            AssertAllLoadResultActionsFalse(decision);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_LoadViewPrefabAsync_DelegatesNewLoadResultDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerPrefabLoadResultPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager new prefab load result decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var loadViewPrefabAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<GameObject> LoadViewPrefabAsync(");

            Assert.That(loadViewPrefabAsync, Does.Contain("handle = prefabRef.LoadAssetAsync<GameObject>();"));
            Assert.That(loadViewPrefabAsync, Does.Contain("await handle.Task;"));
            Assert.That(loadViewPrefabAsync, Does.Contain("var loadOperationSucceeded = handle.Status == AsyncOperationStatus.Succeeded;"));
            Assert.That(loadViewPrefabAsync, Does.Contain("UIManagerPrefabLoadResultPolicy.Resolve("));
            Assert.That(loadViewPrefabAsync, Does.Contain("loadSucceeded: loadOperationSucceeded"));
            Assert.That(loadViewPrefabAsync, Does.Contain("if (loadResultDecision.CacheLoadedHandle)"));
            Assert.That(loadViewPrefabAsync, Does.Contain("_prefabHandles[handleKey] = handle;"));
            Assert.That(loadViewPrefabAsync, Does.Contain("if (loadResultDecision.MarkLoadSucceeded)"));
            Assert.That(loadViewPrefabAsync, Does.Contain("loadSucceeded = true;"));
            Assert.That(loadViewPrefabAsync, Does.Contain("if (loadResultDecision.UseLoadedPrefab)"));
            Assert.That(loadViewPrefabAsync, Does.Contain("return handle.Result;"));
            Assert.That(loadViewPrefabAsync, Does.Not.Contain("if (handle.Status == AsyncOperationStatus.Succeeded)"));
            Assert.That(loadViewPrefabAsync, Does.Not.Contain("handle.Result != null"));
            AssertOrder(
                loadViewPrefabAsync,
                "await handle.Task;",
                "var loadOperationSucceeded = handle.Status == AsyncOperationStatus.Succeeded;",
                "Handle status must be checked only after the load task completes.");
            AssertOrder(
                loadViewPrefabAsync,
                "var loadOperationSucceeded = handle.Status == AsyncOperationStatus.Succeeded;",
                "UIManagerPrefabLoadResultPolicy.Resolve(",
                "The policy decision must use the completed handle status.");
            AssertOrder(
                loadViewPrefabAsync,
                "UIManagerPrefabLoadResultPolicy.Resolve(",
                "_prefabHandles[handleKey] = handle;",
                "Handle caching must only run after the policy decision.");
            AssertOrder(
                loadViewPrefabAsync,
                "UIManagerPrefabLoadResultPolicy.Resolve(",
                "return handle.Result;",
                "The loaded result must only be returned after the policy decision.");

            Assert.That(policySource, Does.Contain("public static UIManagerPrefabLoadResultDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationHandle"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("_prefabHandles"));
            Assert.That(policySource, Does.Not.Contain("handle.Result"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationStatus"));
            Assert.That(policySource, Does.Not.Contain("LoadingTelemetryRecorder"));
        }

        private static void AssertAllLoadResultActionsFalse(UIManagerPrefabLoadResultDecision decision)
        {
            Assert.That(decision.CacheLoadedHandle, Is.False);
            Assert.That(decision.MarkLoadSucceeded, Is.False);
            Assert.That(decision.UseLoadedPrefab, Is.False);
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


