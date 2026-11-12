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
    public sealed class UIManagerPrefabHandlesReleasePolicyTests
    {
        [Test]
        public void Resolve_ValidHandle_ReleasesHandle()
        {
            var decision = UIManagerPrefabHandlesReleasePolicy.Resolve(handleIsValid: true);

            Assert.That(decision.ReleaseHandle, Is.True);
        }

        [Test]
        public void Resolve_InvalidHandle_DoesNotReleaseHandle()
        {
            var decision = UIManagerPrefabHandlesReleasePolicy.Resolve(handleIsValid: false);

            Assert.That(decision.ReleaseHandle, Is.False);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_ReleasePrefabHandles_DelegatesBulkHandleReleaseDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerPrefabHandlesReleasePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager bulk prefab handle release decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var releasePrefabHandles = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private void ReleasePrefabHandles()");

            Assert.That(releasePrefabHandles, Does.Contain("foreach (var handle in _prefabHandles.Values)"));
            Assert.That(releasePrefabHandles, Does.Contain("var handleIsValid = handle.IsValid();"));
            Assert.That(releasePrefabHandles, Does.Contain("UIManagerPrefabHandlesReleasePolicy.Resolve("));
            Assert.That(releasePrefabHandles, Does.Contain("handleIsValid: handleIsValid"));
            Assert.That(releasePrefabHandles, Does.Contain("if (releaseDecision.ReleaseHandle)"));
            Assert.That(releasePrefabHandles, Does.Contain("Addressables.Release(handle);"));
            Assert.That(releasePrefabHandles, Does.Contain("_prefabHandles.Clear();"));
            Assert.That(releasePrefabHandles, Does.Contain("_viewHandleKeys.Clear();"));
            Assert.That(releasePrefabHandles, Does.Not.Contain("if (handle.IsValid())"));
            AssertOrder(
                releasePrefabHandles,
                "var handleIsValid = handle.IsValid();",
                "UIManagerPrefabHandlesReleasePolicy.Resolve(",
                "Handle validity must be captured before the policy decision.");
            AssertOrder(
                releasePrefabHandles,
                "UIManagerPrefabHandlesReleasePolicy.Resolve(",
                "Addressables.Release(handle);",
                "Addressables release must remain after the policy decision.");
            AssertOrder(
                releasePrefabHandles,
                "Addressables.Release(handle);",
                "_prefabHandles.Clear();",
                "Cached handles must still be cleared after release attempts.");
            AssertOrder(
                releasePrefabHandles,
                "_prefabHandles.Clear();",
                "_viewHandleKeys.Clear();",
                "View handle keys must still be cleared after cached handles.");

            Assert.That(policySource, Does.Contain("public static UIManagerPrefabHandlesReleaseDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationHandle"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("_prefabHandles"));
            Assert.That(policySource, Does.Not.Contain("_viewHandleKeys"));
            Assert.That(policySource, Does.Not.Contain("handle.IsValid"));
            Assert.That(policySource, Does.Not.Contain("Addressables.Release"));
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


