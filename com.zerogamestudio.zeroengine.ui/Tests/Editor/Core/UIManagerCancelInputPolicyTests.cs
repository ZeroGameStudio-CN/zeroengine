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
    public sealed class UIManagerCancelInputPolicyTests
    {
        [Test]
        public void Resolve_TopViewAllowsEscClose_ClosesAndConsumes()
        {
            var decision = UIManagerCancelInputPolicy.Resolve(
                hasTopView: true,
                topViewConsumedInput: false,
                allowEscClose: true);

            Assert.That(decision.CloseTopView, Is.True);
            Assert.That(decision.ConsumeInput, Is.True);
        }

        [Test]
        public void Resolve_NoTopView_DoesNotCloseOrConsume()
        {
            var decision = UIManagerCancelInputPolicy.Resolve(
                hasTopView: false,
                topViewConsumedInput: false,
                allowEscClose: true);

            Assert.That(decision.CloseTopView, Is.False);
            Assert.That(decision.ConsumeInput, Is.False);
        }

        [Test]
        public void Resolve_EscCloseNotAllowed_DoesNotCloseOrConsume()
        {
            var decision = UIManagerCancelInputPolicy.Resolve(
                hasTopView: true,
                topViewConsumedInput: false,
                allowEscClose: false);

            Assert.That(decision.CloseTopView, Is.False);
            Assert.That(decision.ConsumeInput, Is.False);
        }

        [Test]
        public void Resolve_TopViewConsumesCancel_DoesNotCloseAndConsumes()
        {
            var decision = UIManagerCancelInputPolicy.Resolve(
                hasTopView: true,
                topViewConsumedInput: true,
                allowEscClose: true);

            Assert.That(decision.CloseTopView, Is.False);
            Assert.That(decision.ConsumeInput, Is.True);
        }

        [Test]
        public void UIViewBase_DefaultCancelHook_DoesNotConsume()
        {
            var gameObject = new GameObject(nameof(DefaultCancelHookView));
            try
            {
                var view = gameObject.AddComponent<DefaultCancelHookView>();

                Assert.That(view.TryHandleCancelInput(), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_TryHandleCancelInput_DelegatesCloseDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerCancelInputPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager cancel input decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var handleCancelInput = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "public bool TryHandleCancelInput()");

            Assert.That(handleCancelInput, Does.Contain("UIManagerCancelInputPolicy.Resolve("));
            Assert.That(handleCancelInput, Does.Contain("_topView == null"));
            Assert.That(handleCancelInput, Does.Contain("topView.TryHandleCancelInput()"));
            Assert.That(handleCancelInput, Does.Not.Contain("_topView == null || !_topView.Config.allowESCClose"));
            AssertOrder(
                handleCancelInput,
                "_topView == null",
                "topView.TryHandleCancelInput()",
                "The top view cancel hook must run only after the null guard.");
            AssertOrder(
                handleCancelInput,
                "topView.TryHandleCancelInput()",
                "topView.Config.allowESCClose",
                "The top view must get the first chance to consume cancel before database close policy is evaluated.");
            AssertOrder(
                handleCancelInput,
                "CloseTop();",
                "return cancelInputDecision.ConsumeInput;",
                "Cancel input must close the top view before reporting the input as consumed.");

            Assert.That(policySource, Does.Contain("public static UIManagerCancelInputDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("_topView"));
            Assert.That(policySource, Does.Not.Contain("Config"));
            Assert.That(policySource, Does.Not.Contain("allowESCClose"));
            Assert.That(policySource, Does.Not.Contain("CloseTop();"));
        }

        private sealed class DefaultCancelHookView : UIViewBase
        {
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
