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
    public sealed class UIManagerExistingPrefabHandlePolicyTests
    {
        [Test]
        public void Resolve_AllInputsTrue_UsesExistingPrefab()
        {
            var decision = UIManagerExistingPrefabHandlePolicy.Resolve(
                handleIsValid: true,
                loadSucceeded: true,
                hasPrefabResult: true);

            AssertDecision(
                decision,
                awaitExistingHandle: true,
                useExistingPrefab: true);
        }

        [Test]
        public void Resolve_InvalidHandle_DoesNotUseExistingPrefab()
        {
            var decision = UIManagerExistingPrefabHandlePolicy.Resolve(
                handleIsValid: false,
                loadSucceeded: true,
                hasPrefabResult: true);

            AssertDecision(
                decision,
                awaitExistingHandle: false,
                useExistingPrefab: false);
        }

        [Test]
        public void Resolve_LoadNotSucceeded_DoesNotUseExistingPrefab()
        {
            var decision = UIManagerExistingPrefabHandlePolicy.Resolve(
                handleIsValid: true,
                loadSucceeded: false,
                hasPrefabResult: true);

            AssertDecision(
                decision,
                awaitExistingHandle: true,
                useExistingPrefab: false);
        }

        [Test]
        public void Resolve_MissingPrefabResult_DoesNotUseExistingPrefab()
        {
            var decision = UIManagerExistingPrefabHandlePolicy.Resolve(
                handleIsValid: true,
                loadSucceeded: true,
                hasPrefabResult: false);

            AssertDecision(
                decision,
                awaitExistingHandle: true,
                useExistingPrefab: false);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_TryGetPrefabFromExistingReferenceHandleAsync_DelegatesExistingHandleEligibilityToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerExistingPrefabHandlePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager existing prefab handle eligibility must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var tryGetExistingHandle = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task<GameObject> TryGetPrefabFromExistingReferenceHandleAsync(");

            Assert.That(tryGetExistingHandle, Does.Contain("var existingHandle = prefabRef.OperationHandle;"));
            Assert.That(tryGetExistingHandle, Does.Contain("var handleIsValid = existingHandle.IsValid();"));
            Assert.That(tryGetExistingHandle, Does.Contain("UIManagerExistingPrefabHandlePolicy.Resolve("));
            Assert.That(tryGetExistingHandle, Does.Contain("loadSucceeded: false"));
            Assert.That(tryGetExistingHandle, Does.Contain("hasPrefabResult: false"));
            Assert.That(tryGetExistingHandle, Does.Contain("if (!existingHandleDecision.AwaitExistingHandle)"));
            Assert.That(tryGetExistingHandle, Does.Not.Contain("if (!handleIsValid)"));
            Assert.That(tryGetExistingHandle, Does.Contain("await existingHandle.Task;"));
            Assert.That(tryGetExistingHandle, Does.Contain("var loadSucceeded = existingHandle.Status == AsyncOperationStatus.Succeeded;"));
            Assert.That(tryGetExistingHandle, Does.Contain("var existingPrefab = loadSucceeded ? existingHandle.Result as GameObject : null;"));
            Assert.That(tryGetExistingHandle, Does.Contain("UIManagerExistingPrefabHandlePolicy.Resolve("));
            Assert.That(tryGetExistingHandle, Does.Contain("handleIsValid: handleIsValid"));
            Assert.That(tryGetExistingHandle, Does.Contain("loadSucceeded: loadSucceeded"));
            Assert.That(tryGetExistingHandle, Does.Contain("hasPrefabResult: existingPrefab != null"));
            Assert.That(tryGetExistingHandle, Does.Contain("_prefabHandles[handleKey] = existingHandle.Convert<GameObject>();"));
            Assert.That(tryGetExistingHandle, Does.Contain("return existingPrefab;"));
            Assert.That(tryGetExistingHandle, Does.Not.Contain("existingHandle.Status != AsyncOperationStatus.Succeeded"));
            Assert.That(tryGetExistingHandle, Does.Not.Contain("existingHandle.Result is not GameObject"));
            AssertOrder(
                tryGetExistingHandle,
                "var existingHandle = prefabRef.OperationHandle;",
                "var handleIsValid = existingHandle.IsValid();",
                "The existing handle must be read before validity is checked.");
            AssertOrder(
                tryGetExistingHandle,
                "var handleIsValid = existingHandle.IsValid();",
                "UIManagerExistingPrefabHandlePolicy.Resolve(",
                "Handle validity must be classified before deciding whether to await the existing handle.");
            AssertOrder(
                tryGetExistingHandle,
                "if (!existingHandleDecision.AwaitExistingHandle)",
                "await existingHandle.Task;",
                "The existing handle must not be awaited until the policy allows it.");
            AssertOrder(
                tryGetExistingHandle,
                "await existingHandle.Task;",
                "var loadSucceeded = existingHandle.Status == AsyncOperationStatus.Succeeded;",
                "Existing handle status must be checked after its task completes.");
            AssertOrder(
                tryGetExistingHandle,
                "var loadSucceeded = existingHandle.Status == AsyncOperationStatus.Succeeded;",
                "var existingPrefab = loadSucceeded ? existingHandle.Result as GameObject : null;",
                "Existing handle result must only be read after the load succeeded.");
            AssertOrder(
                tryGetExistingHandle,
                "UIManagerExistingPrefabHandlePolicy.Resolve(",
                "_prefabHandles[handleKey] = existingHandle.Convert<GameObject>();",
                "Cache registration must only run after the policy decision.");

            Assert.That(policySource, Does.Contain("public static UIManagerExistingPrefabHandleDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationHandle"));
            Assert.That(policySource, Does.Not.Contain("AssetReference"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("_prefabHandles"));
            Assert.That(policySource, Does.Not.Contain("existingHandle"));
            Assert.That(policySource, Does.Not.Contain(".Result"));
            Assert.That(policySource, Does.Not.Contain(".IsValid("));
            Assert.That(policySource, Does.Not.Contain("AsyncOperationStatus"));
            Assert.That(policySource, Does.Not.Contain("Convert<GameObject>"));
        }

        private static void AssertDecision(
            UIManagerExistingPrefabHandleDecision decision,
            bool awaitExistingHandle,
            bool useExistingPrefab)
        {
            Assert.That(decision.AwaitExistingHandle, Is.EqualTo(awaitExistingHandle));
            Assert.That(decision.UseExistingPrefab, Is.EqualTo(useExistingPrefab));
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


