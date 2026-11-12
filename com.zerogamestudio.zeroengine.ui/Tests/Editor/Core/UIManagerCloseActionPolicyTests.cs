using System.IO;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("UI")]
    public sealed class UIManagerCloseActionPolicyTests
    {
        [Test]
        public void Resolve_Evictable_RemovesReleasesAndDestroys()
        {
            var decision = UIManagerCloseActionPolicy.Resolve(UIViewLifetime.Evictable, UICloseMode.Pool);

            AssertDecision(
                decision,
                removeInstance: true,
                releasePrefabHandle: true,
                destroyInstance: true,
                deactivateInstance: false);
        }

        [Test]
        public void Resolve_Destroy_RemovesAndDestroysWithoutReleasingPrefabHandle()
        {
            var decision = UIManagerCloseActionPolicy.Resolve(UIViewLifetime.Session, UICloseMode.Destroy);

            AssertDecision(
                decision,
                removeInstance: true,
                releasePrefabHandle: false,
                destroyInstance: true,
                deactivateInstance: false);
        }

        [Test]
        public void Resolve_Pool_DeactivatesOnly()
        {
            var decision = UIManagerCloseActionPolicy.Resolve(UIViewLifetime.Resident, UICloseMode.Pool);

            AssertDecision(
                decision,
                removeInstance: false,
                releasePrefabHandle: false,
                destroyInstance: false,
                deactivateInstance: true);
        }

        [Test]
        public void Resolve_Hide_DoesNotApplyCloseModeSideEffects()
        {
            var decision = UIManagerCloseActionPolicy.Resolve(UIViewLifetime.Session, UICloseMode.Hide);

            AssertDecision(
                decision,
                removeInstance: false,
                releasePrefabHandle: false,
                destroyInstance: false,
                deactivateInstance: false);
        }

        [Test]
        public void Resolve_UnknownCloseMode_DoesNotApplyCloseModeSideEffects()
        {
            var decision = UIManagerCloseActionPolicy.Resolve(UIViewLifetime.Session, (UICloseMode)999);

            AssertDecision(
                decision,
                removeInstance: false,
                releasePrefabHandle: false,
                destroyInstance: false,
                deactivateInstance: false);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_HandleCloseMode_DelegatesCloseDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerCloseActionPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager close-mode decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var handleCloseMode = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private async Task HandleCloseMode(UIViewBase view, UIViewConfig config)");

            Assert.That(handleCloseMode, Does.Contain("UIManagerCloseActionPolicy.Resolve("));
            Assert.That(handleCloseMode, Does.Not.Contain("config.lifetime == UIViewLifetime.Evictable"));
            Assert.That(handleCloseMode, Does.Not.Contain("case UICloseMode.Destroy"));
            Assert.That(handleCloseMode, Does.Not.Contain("case UICloseMode.Pool"));

            Assert.That(policySource, Does.Contain("public static UIManagerCloseActionDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("_viewInstances"));
            Assert.That(policySource, Does.Not.Contain("ReleasePrefabHandleForView"));
            Assert.That(policySource, Does.Not.Contain("DestroyViewInstance"));
            Assert.That(policySource, Does.Not.Contain("gameObject"));
            Assert.That(policySource, Does.Not.Contain("SetActive"));
        }

        private static void AssertDecision(
            UIManagerCloseActionDecision decision,
            bool removeInstance,
            bool releasePrefabHandle,
            bool destroyInstance,
            bool deactivateInstance)
        {
            Assert.That(decision.RemoveInstance, Is.EqualTo(removeInstance));
            Assert.That(decision.ReleasePrefabHandle, Is.EqualTo(releasePrefabHandle));
            Assert.That(decision.DestroyInstance, Is.EqualTo(destroyInstance));
            Assert.That(decision.DeactivateInstance, Is.EqualTo(deactivateInstance));
        }

        private static string AssetPath(params string[] parts)
        {
            var path = Path.Combine(parts);
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "com.zerogamestudio.zeroengine.ui", path);
        }
    }
}


