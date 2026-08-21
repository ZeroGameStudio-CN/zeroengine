using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.UI;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("UI")]
    public sealed class UIManagerLayerTraversalPolicyTests
    {
        [Test]
        public void GetTopViewSearchOrder_ReturnsLayersInDescendingValueOrder()
        {
            var layers = UIManagerLayerTraversalPolicy.GetTopViewSearchOrder();

            for (var index = 1; index < layers.Count; index++)
            {
                Assert.That((int)layers[index - 1], Is.GreaterThan((int)layers[index]));
            }
        }

        [Test]
        public void GetTopViewSearchOrder_ContainsEveryLayerOnce()
        {
            var expectedLayers = System.Enum.GetValues(typeof(UILayer)).Cast<UILayer>().OrderBy(layer => layer).ToArray();
            var actualLayers = UIManagerLayerTraversalPolicy.GetTopViewSearchOrder().OrderBy(layer => layer).ToArray();

            CollectionAssert.AreEqual(expectedLayers, actualLayers);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_UpdateTopView_UsesCachedLayerTraversalPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerLayerTraversalPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager layer traversal order must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var updateTopView = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private void UpdateTopView()");

            Assert.That(updateTopView, Does.Contain("_topView = null;"));
            Assert.That(updateTopView, Does.Contain("UIManagerLayerTraversalPolicy.GetTopViewSearchOrder()"));
            Assert.That(updateTopView, Does.Contain("_layerStacks.TryGetValue(layer, out var stack) && stack.Count > 0"));
            Assert.That(updateTopView, Does.Contain("_topView = stack.Peek();"));
            Assert.That(updateTopView, Does.Contain("return;"));
            Assert.That(updateTopView, Does.Not.Contain("Enum.GetValues"));
            Assert.That(updateTopView, Does.Not.Contain("Array.Sort"));
            AssertOrder(
                updateTopView,
                "_topView = null;",
                "UIManagerLayerTraversalPolicy.GetTopViewSearchOrder()",
                "UpdateTopView must clear the top view before searching layer stacks.");
            AssertOrder(
                updateTopView,
                "_layerStacks.TryGetValue(layer, out var stack) && stack.Count > 0",
                "_topView = stack.Peek();",
                "UpdateTopView must prove the stack has a view before peeking it.");

            Assert.That(policySource, Does.Contain("Enum.GetValues(typeof(UILayer))"));
            Assert.That(policySource, Does.Contain("Array.Sort"));
            Assert.That(policySource, Does.Not.Contain("_layerStacks"));
            Assert.That(policySource, Does.Not.Contain("_topView"));
            Assert.That(policySource, Does.Not.Contain("UIViewBase"));
            Assert.That(policySource, Does.Not.Contain("Stack<"));
            Assert.That(policySource, Does.Not.Contain("stack.Peek"));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
        }

        private static void AssertOrder(string source, string first, string second, string message)
        {
            var firstIndex = source.IndexOf(first, System.StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, System.StringComparison.Ordinal);

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


