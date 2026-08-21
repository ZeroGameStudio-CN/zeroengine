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
    public sealed class UIManagerPrefabHandleReleasePolicyTests
    {
        [Test]
        public void Resolve_MissingViewHandleKey_DoesNothing()
        {
            var decision = UIManagerPrefabHandleReleasePolicy.Resolve(
                hasViewHandleKey: false,
                handleUsedByOtherView: false,
                hasCachedHandle: true,
                cachedHandleIsValid: true);

            AssertAllReleaseActionsFalse(decision);
        }

        [Test]
        public void Resolve_SharedHandle_RemovesViewKeyOnly()
        {
            var decision = UIManagerPrefabHandleReleasePolicy.Resolve(
                hasViewHandleKey: true,
                handleUsedByOtherView: true,
                hasCachedHandle: true,
                cachedHandleIsValid: true);

            Assert.That(decision.RemoveViewHandleKey, Is.True);
            Assert.That(decision.RemoveCachedHandle, Is.False);
            Assert.That(decision.ReleaseCachedHandle, Is.False);
        }

        [Test]
        public void Resolve_MissingCachedHandle_RemovesViewKeyOnly()
        {
            var decision = UIManagerPrefabHandleReleasePolicy.Resolve(
                hasViewHandleKey: true,
                handleUsedByOtherView: false,
                hasCachedHandle: false,
                cachedHandleIsValid: true);

            Assert.That(decision.RemoveViewHandleKey, Is.True);
            Assert.That(decision.RemoveCachedHandle, Is.False);
            Assert.That(decision.ReleaseCachedHandle, Is.False);
        }

        [Test]
        public void Resolve_UnsharedCachedHandle_RemovesViewKeyAndCachedHandle()
        {
            var decision = UIManagerPrefabHandleReleasePolicy.Resolve(
                hasViewHandleKey: true,
                handleUsedByOtherView: false,
                hasCachedHandle: true,
                cachedHandleIsValid: true);

            Assert.That(decision.RemoveViewHandleKey, Is.True);
            Assert.That(decision.RemoveCachedHandle, Is.True);
            Assert.That(decision.ReleaseCachedHandle, Is.True);
        }

        [Test]
        public void Resolve_InvalidCachedHandle_RemovesViewKeyAndCachedHandleButDoesNotRelease()
        {
            var decision = UIManagerPrefabHandleReleasePolicy.Resolve(
                hasViewHandleKey: true,
                handleUsedByOtherView: false,
                hasCachedHandle: true,
                cachedHandleIsValid: false);

            Assert.That(decision.RemoveViewHandleKey, Is.True);
            Assert.That(decision.RemoveCachedHandle, Is.True);
            Assert.That(decision.ReleaseCachedHandle, Is.False);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_ReleasePrefabHandleForView_DelegatesReleaseDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerPrefabHandleReleasePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager prefab handle release decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var releasePrefabHandleForView = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private void ReleasePrefabHandleForView(string viewName)");
            var handleUsedByOtherView = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private bool IsPrefabHandleUsedByOtherView(string viewName, string handleKey)");

            Assert.That(releasePrefabHandleForView, Does.Contain("_viewHandleKeys.TryGetValue(viewName, out var handleKey)"));
            Assert.That(releasePrefabHandleForView, Does.Contain("IsPrefabHandleUsedByOtherView(viewName, handleKey)"));
            Assert.That(releasePrefabHandleForView, Does.Contain("_prefabHandles.TryGetValue(handleKey, out var handle)"));
            Assert.That(releasePrefabHandleForView, Does.Contain("var cachedHandleIsValid = hasCachedHandle && handle.IsValid();"));
            Assert.That(releasePrefabHandleForView, Does.Contain("UIManagerPrefabHandleReleasePolicy.Resolve("));
            Assert.That(releasePrefabHandleForView, Does.Contain("hasViewHandleKey: true"));
            Assert.That(releasePrefabHandleForView, Does.Contain("handleUsedByOtherView: handleUsedByOtherView"));
            Assert.That(releasePrefabHandleForView, Does.Contain("hasCachedHandle: hasCachedHandle"));
            Assert.That(releasePrefabHandleForView, Does.Contain("cachedHandleIsValid: cachedHandleIsValid"));
            Assert.That(releasePrefabHandleForView, Does.Contain("_viewHandleKeys.Remove(viewName)"));
            Assert.That(releasePrefabHandleForView, Does.Contain("_prefabHandles.Remove(handleKey)"));
            Assert.That(releasePrefabHandleForView, Does.Contain("if (releaseDecision.ReleaseCachedHandle)"));
            Assert.That(releasePrefabHandleForView, Does.Not.Contain("releaseDecision.ReleaseCachedHandle && handle.IsValid()"));
            Assert.That(releasePrefabHandleForView, Does.Contain("Addressables.Release(handle)"));
            Assert.That(releasePrefabHandleForView, Does.Not.Contain("foreach (var activeHandleKey in _viewHandleKeys.Values)"));
            AssertOrder(
                releasePrefabHandleForView,
                "_viewHandleKeys.TryGetValue(viewName, out var handleKey)",
                "IsPrefabHandleUsedByOtherView(viewName, handleKey)",
                "The handle key must be read before shared-handle detection.");
            AssertOrder(
                releasePrefabHandleForView,
                "_prefabHandles.TryGetValue(handleKey, out var handle)",
                "UIManagerPrefabHandleReleasePolicy.Resolve(",
                "Cached-handle presence must be resolved before the release policy decision.");
            AssertOrder(
                releasePrefabHandleForView,
                "var cachedHandleIsValid = hasCachedHandle && handle.IsValid();",
                "UIManagerPrefabHandleReleasePolicy.Resolve(",
                "Cached handle validity must be captured before the release policy decision.");
            AssertOrder(
                releasePrefabHandleForView,
                "_viewHandleKeys.Remove(viewName)",
                "_prefabHandles.Remove(handleKey)",
                "The view handle key should remain the first release side effect.");
            AssertOrder(
                releasePrefabHandleForView,
                "_prefabHandles.Remove(handleKey)",
                "Addressables.Release(handle)",
                "The cached handle must be removed before Addressables release.");

            Assert.That(handleUsedByOtherView, Does.Contain("foreach (var viewHandleKey in _viewHandleKeys)"));
            Assert.That(handleUsedByOtherView, Does.Contain("viewHandleKey.Key != viewName && viewHandleKey.Value == handleKey"));
            Assert.That(handleUsedByOtherView, Does.Not.Contain("_viewHandleKeys.Values"));

            Assert.That(policySource, Does.Contain("public static UIManagerPrefabHandleReleaseDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationHandle"));
            Assert.That(policySource, Does.Not.Contain("_viewHandleKeys"));
            Assert.That(policySource, Does.Not.Contain("_prefabHandles"));
            Assert.That(policySource, Does.Not.Contain("Addressables.Release"));
            Assert.That(policySource, Does.Not.Contain("IsValid()"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
        }

        private static void AssertAllReleaseActionsFalse(UIManagerPrefabHandleReleaseDecision decision)
        {
            Assert.That(decision.RemoveViewHandleKey, Is.False);
            Assert.That(decision.RemoveCachedHandle, Is.False);
            Assert.That(decision.ReleaseCachedHandle, Is.False);
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


