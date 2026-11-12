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
    public sealed class UIManagerViewRegistrationPolicyTests
    {
        [Test]
        public void Resolve_EmptyViewName_LogsAndSkipsStorage()
        {
            var decision = UIManagerViewRegistrationPolicy.Resolve(
                viewNameIsEmpty: true,
                alreadyRegistered: false);

            AssertDecision(
                decision,
                logViewNameEmpty: true,
                logViewAlreadyRegistered: false,
                storeConfig: false,
                returnAfterEmptyName: true);
        }

        [Test]
        public void Resolve_NewViewName_StoresWithoutLogging()
        {
            var decision = UIManagerViewRegistrationPolicy.Resolve(
                viewNameIsEmpty: false,
                alreadyRegistered: false);

            AssertDecision(
                decision,
                logViewNameEmpty: false,
                logViewAlreadyRegistered: false,
                storeConfig: true,
                returnAfterEmptyName: false);
        }

        [Test]
        public void Resolve_DuplicateViewName_LogsAndStores()
        {
            var decision = UIManagerViewRegistrationPolicy.Resolve(
                viewNameIsEmpty: false,
                alreadyRegistered: true);

            AssertDecision(
                decision,
                logViewNameEmpty: false,
                logViewAlreadyRegistered: true,
                storeConfig: true,
                returnAfterEmptyName: false);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_RegisterView_DelegatesRegistrationDecisionToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerViewRegistrationPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager view registration decisions must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var registerView = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "public void RegisterView(UIViewConfig config)");

            Assert.That(registerView, Does.Contain("var viewNameIsEmpty = string.IsNullOrEmpty(config.viewName);"));
            Assert.That(registerView, Does.Contain("var alreadyRegistered = !viewNameIsEmpty && _viewConfigs.ContainsKey(config.viewName);"));
            Assert.That(registerView, Does.Contain("UIManagerViewRegistrationPolicy.Resolve("));
            Assert.That(registerView, Does.Contain("viewNameIsEmpty: viewNameIsEmpty"));
            Assert.That(registerView, Does.Contain("alreadyRegistered: alreadyRegistered"));
            Assert.That(registerView, Does.Contain("if (registrationDecision.LogViewNameEmpty)"));
            Assert.That(registerView, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewNameEmpty())"));
            Assert.That(registerView, Does.Contain("if (registrationDecision.ReturnAfterEmptyName)"));
            Assert.That(registerView, Does.Contain("if (registrationDecision.LogViewAlreadyRegistered)"));
            Assert.That(registerView, Does.Contain("LogUIManager(UIManagerLogPolicy.ViewAlreadyRegistered(config.viewName))"));
            Assert.That(registerView, Does.Contain("if (registrationDecision.StoreConfig)"));
            Assert.That(registerView, Does.Contain("_viewConfigs[config.viewName] = config;"));
            Assert.That(registerView, Does.Not.Contain("if (string.IsNullOrEmpty(config.viewName))"));
            Assert.That(registerView, Does.Not.Contain("if (_viewConfigs.ContainsKey(config.viewName))"));

            AssertOrder(
                registerView,
                "var viewNameIsEmpty = string.IsNullOrEmpty(config.viewName);",
                "var alreadyRegistered = !viewNameIsEmpty && _viewConfigs.ContainsKey(config.viewName);",
                "Duplicate lookup must not run before empty-name classification.");
            AssertOrderAfter(
                registerView,
                "UIManagerViewRegistrationPolicy.Resolve(",
                "if (registrationDecision.LogViewNameEmpty)",
                "LogUIManager(UIManagerLogPolicy.ViewNameEmpty())",
                "Policy decision must be resolved before empty-name logging.");
            AssertOrder(
                registerView,
                "if (registrationDecision.ReturnAfterEmptyName)",
                "LogUIManager(UIManagerLogPolicy.ViewAlreadyRegistered(config.viewName))",
                "Empty-name return must stay before duplicate logging.");
            AssertOrder(
                registerView,
                "LogUIManager(UIManagerLogPolicy.ViewAlreadyRegistered(config.viewName))",
                "_viewConfigs[config.viewName] = config;",
                "Duplicate registration must still log before overwriting the config.");

            Assert.That(policySource, Does.Contain("public static UIManagerViewRegistrationDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("UIViewConfig"));
            Assert.That(policySource, Does.Not.Contain("_viewConfigs"));
            Assert.That(policySource, Does.Not.Contain("LogUIManager"));
            Assert.That(policySource, Does.Not.Contain("UIManagerLogPolicy"));
        }

        private static void AssertDecision(
            UIManagerViewRegistrationDecision decision,
            bool logViewNameEmpty,
            bool logViewAlreadyRegistered,
            bool storeConfig,
            bool returnAfterEmptyName)
        {
            Assert.That(decision.LogViewNameEmpty, Is.EqualTo(logViewNameEmpty));
            Assert.That(decision.LogViewAlreadyRegistered, Is.EqualTo(logViewAlreadyRegistered));
            Assert.That(decision.StoreConfig, Is.EqualTo(storeConfig));
            Assert.That(decision.ReturnAfterEmptyName, Is.EqualTo(returnAfterEmptyName));
        }

        private static void AssertOrder(string source, string first, string second, string message)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

            Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), $"Missing first marker: {first}");
            Assert.That(secondIndex, Is.GreaterThanOrEqualTo(0), $"Missing second marker: {second}");
            Assert.That(firstIndex, Is.LessThan(secondIndex), message);
        }

        private static void AssertOrderAfter(
            string source,
            string anchor,
            string first,
            string second,
            string message)
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
