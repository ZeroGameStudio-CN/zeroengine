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
    public sealed class UIManagerSessionViewReleasePolicyTests
    {
        [Test]
        public void Resolve_MissingView_DoesNotRelease()
        {
            var decision = UIManagerSessionViewReleasePolicy.Resolve(
                hasView: false,
                isResident: false,
                showMask: true);

            AssertAllCleanupFalse(decision);
        }

        [Test]
        public void Resolve_ResidentView_DoesNotRelease()
        {
            var decision = UIManagerSessionViewReleasePolicy.Resolve(
                hasView: true,
                isResident: true,
                showMask: true);

            AssertAllCleanupFalse(decision);
        }

        [Test]
        public void Resolve_NonResidentWithoutMask_ReleasesWithoutHidingMask()
        {
            var decision = UIManagerSessionViewReleasePolicy.Resolve(
                hasView: true,
                isResident: false,
                showMask: false);

            Assert.That(decision.ReleaseView, Is.True);
            Assert.That(decision.RemoveInstance, Is.True);
            Assert.That(decision.RemoveFromStack, Is.True);
            Assert.That(decision.HideMask, Is.False);
            Assert.That(decision.DestroyInstance, Is.True);
            Assert.That(decision.ReleasePrefabHandle, Is.True);
        }

        [Test]
        public void Resolve_NonResidentWithMask_ReleasesAndHidesMask()
        {
            var decision = UIManagerSessionViewReleasePolicy.Resolve(
                hasView: true,
                isResident: false,
                showMask: true);

            Assert.That(decision.ReleaseView, Is.True);
            Assert.That(decision.RemoveInstance, Is.True);
            Assert.That(decision.RemoveFromStack, Is.True);
            Assert.That(decision.HideMask, Is.True);
            Assert.That(decision.DestroyInstance, Is.True);
            Assert.That(decision.ReleasePrefabHandle, Is.True);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_ReleaseSessionViews_DelegatesCleanupDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerSessionViewReleasePolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager session view release decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var releaseSessionViews = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "public void ReleaseSessionViews()");

            Assert.That(releaseSessionViews, Does.Contain("_sessionViewGeneration++;"));
            Assert.That(releaseSessionViews, Does.Contain("new List<string>(_viewInstances.Keys)"));
            Assert.That(releaseSessionViews, Does.Contain("UIManagerSessionViewReleasePolicy.Resolve("));
            Assert.That(releaseSessionViews, Does.Contain("_viewInstances.TryGetValue(viewName, out var view)"));
            Assert.That(releaseSessionViews, Does.Contain("view == null"));
            Assert.That(releaseSessionViews, Does.Not.Contain("|| view.Config.lifetime == UIViewLifetime.Resident"));
            AssertOrder(
                releaseSessionViews,
                "_viewInstances.TryGetValue(viewName, out var view)",
                "view == null",
                "view must not be inspected before the dictionary lookup.");
            AssertOrder(
                releaseSessionViews,
                "view == null",
                "var config = view.Config;",
                "view.Config must not be evaluated before the null guard.");
            Assert.That(releaseSessionViews, Does.Contain("_viewInstances.Remove(viewName)"));
            Assert.That(releaseSessionViews, Does.Contain("RemoveFromStack(view, config.layer)"));
            Assert.That(releaseSessionViews, Does.Contain("RefreshMask(config.layer)"));
            Assert.That(releaseSessionViews, Does.Contain("DestroyViewInstance(view)"));
            Assert.That(releaseSessionViews, Does.Contain("ReleasePrefabHandleForView(viewName)"));
            AssertOrder(
                releaseSessionViews,
                "_sessionViewGeneration++;",
                "new List<string>(_viewInstances.Keys)",
                "Pending session opens must be invalidated before existing session views are released.");
            Assert.That(managerSource, Does.Contain("private bool CanContinueViewOperation(UIViewConfig config, int requestSessionViewGeneration)"));
            Assert.That(managerSource, Does.Contain("config.lifetime == UIViewLifetime.Resident"));
            Assert.That(managerSource, Does.Contain("requestSessionViewGeneration == _sessionViewGeneration"));
            Assert.That(managerSource, Does.Contain("pendingRequest.SessionViewGeneration == requestSessionViewGeneration"));
            Assert.That(managerSource, Does.Contain("return OpenAsync(viewName, args, _sessionViewGeneration);"));

            Assert.That(policySource, Does.Contain("public static UIManagerSessionViewReleaseDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("_viewInstances"));
            Assert.That(policySource, Does.Not.Contain("RemoveFromStack("));
            Assert.That(policySource, Does.Not.Contain("HideMask("));
            Assert.That(policySource, Does.Not.Contain("DestroyViewInstance"));
            Assert.That(policySource, Does.Not.Contain("ReleasePrefabHandleForView"));
            Assert.That(policySource, Does.Not.Contain("Config"));
            Assert.That(policySource, Does.Not.Contain("UIViewLifetime"));
            Assert.That(policySource, Does.Not.Contain("Addressables"));
        }

        private static void AssertAllCleanupFalse(UIManagerSessionViewReleaseDecision decision)
        {
            Assert.That(decision.ReleaseView, Is.False);
            Assert.That(decision.RemoveInstance, Is.False);
            Assert.That(decision.RemoveFromStack, Is.False);
            Assert.That(decision.HideMask, Is.False);
            Assert.That(decision.DestroyInstance, Is.False);
            Assert.That(decision.ReleasePrefabHandle, Is.False);
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

