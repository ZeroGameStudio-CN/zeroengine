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
    public sealed class UIManagerPrefabLoadFailureReleasePolicyTests
    {
        [Test]
        public void Resolve_ValidHandle_ReleasesHandle()
        {
            var decision = UIManagerPrefabLoadFailureReleasePolicy.Resolve(handleIsValid: true);

            Assert.That(decision.ReleaseHandle, Is.True);
        }

        [Test]
        public void Resolve_InvalidHandle_DoesNotReleaseHandle()
        {
            var decision = UIManagerPrefabLoadFailureReleasePolicy.Resolve(handleIsValid: false);

            Assert.That(decision.ReleaseHandle, Is.False);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_LoadViewPrefabAsync_DelegatesFailedHandleReleaseDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerPrefabLoadFailureReleasePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager failed prefab-load handle release decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var loadViewPrefabAsync = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<GameObject> LoadViewPrefabAsync(");

            Assert.That(loadViewPrefabAsync, Does.Contain("LogUIManager(UIManagerLogPolicy.AddressablesLoadFailed(prefabRef.RuntimeKey.ToString()));"));
            Assert.That(loadViewPrefabAsync, Does.Contain("var failedHandleIsValid = handle.IsValid();"));
            Assert.That(loadViewPrefabAsync, Does.Contain("UIManagerPrefabLoadFailureReleasePolicy.Resolve("));
            Assert.That(loadViewPrefabAsync, Does.Contain("handleIsValid: failedHandleIsValid"));
            Assert.That(loadViewPrefabAsync, Does.Contain("if (failureReleaseDecision.ReleaseHandle)"));
            Assert.That(loadViewPrefabAsync, Does.Contain("Addressables.Release(handle);"));
            Assert.That(loadViewPrefabAsync, Does.Contain("return null;"));
            Assert.That(loadViewPrefabAsync, Does.Not.Contain("if (handle.IsValid())"));
            AssertOrder(
                loadViewPrefabAsync,
                "LogUIManager(UIManagerLogPolicy.AddressablesLoadFailed(prefabRef.RuntimeKey.ToString()));",
                "var failedHandleIsValid = handle.IsValid();",
                "The failed-load log must remain before handle validity is read.");
            AssertOrder(
                loadViewPrefabAsync,
                "var failedHandleIsValid = handle.IsValid();",
                "UIManagerPrefabLoadFailureReleasePolicy.Resolve(",
                "The policy decision must use the captured handle validity.");
            AssertOrder(
                loadViewPrefabAsync,
                "UIManagerPrefabLoadFailureReleasePolicy.Resolve(",
                "Addressables.Release(handle);",
                "The release side effect must only run after the policy decision.");
            AssertOrderAfter(
                loadViewPrefabAsync,
                "LogUIManager(UIManagerLogPolicy.AddressablesLoadFailed(prefabRef.RuntimeKey.ToString()));",
                "Addressables.Release(handle);",
                "return null;",
                "The failed-load branch must still return null after any release.");

            Assert.That(policySource, Does.Contain("public static UIManagerPrefabLoadFailureReleaseDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationHandle"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("_prefabHandles"));
            Assert.That(policySource, Does.Not.Contain("handle.IsValid"));
            Assert.That(policySource, Does.Not.Contain("Addressables.Release"));
            Assert.That(policySource, Does.Not.Contain("UIManagerLogPolicy"));
            Assert.That(policySource, Does.Not.Contain("LoadingTelemetryRecorder"));
        }

        private static void AssertOrder(string source, string first, string second, string message)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"Missing first marker: {first}");
            Assert.That(secondIndex, Is.GreaterThanOrEqualTo(0), $"Missing second marker: {second}");
            Assert.That(firstIndex, Is.LessThan(secondIndex), message);
        }

        private static void AssertOrderAfter(string source, string anchor, string first, string second, string message)
        {
            var anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
            Assert.That(anchorIndex, Is.GreaterThanOrEqualTo(0), $"Missing anchor marker: {anchor}");

            var firstIndex = source.IndexOf(first, anchorIndex, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, anchorIndex, StringComparison.Ordinal);

            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"Missing first marker after anchor: {first}");
            Assert.That(secondIndex, Is.GreaterThanOrEqualTo(0), $"Missing second marker after anchor: {second}");
            Assert.That(firstIndex, Is.LessThan(secondIndex), message);
        }

        private static string AssetPath(params string[] parts)
        {
            var path = Path.Combine(parts);
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "com.zerogamestudio.zeroengine.ui", path);
        }
    }
}


