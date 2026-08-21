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
    public sealed class UIManagerMaskActionPolicyTests
    {
        [Test]
        public void Resolve_MissingPrefab_DoesNothing()
        {
            var decision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab: false,
                hasExistingMask: false,
                hasImage: true,
                hasButton: true,
                hasClickAction: true);

            AssertDecision(
                decision,
                useMask: false,
                createMask: false,
                positionMask: false,
                applyColor: false,
                clearClickListeners: false,
                addClickListener: false,
                activateMask: false);
        }

        [Test]
        public void Resolve_PrefabWithoutExistingMask_CreatesPositionsAndActivates()
        {
            var decision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab: true,
                hasExistingMask: false,
                hasImage: false,
                hasButton: false,
                hasClickAction: false);

            AssertDecision(
                decision,
                useMask: true,
                createMask: true,
                positionMask: true,
                applyColor: false,
                clearClickListeners: false,
                addClickListener: false,
                activateMask: true);
        }

        [Test]
        public void Resolve_ExistingMaskWithoutComponents_PositionsAndActivatesOnly()
        {
            var decision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab: true,
                hasExistingMask: true,
                hasImage: false,
                hasButton: false,
                hasClickAction: true);

            AssertDecision(
                decision,
                useMask: true,
                createMask: false,
                positionMask: true,
                applyColor: false,
                clearClickListeners: false,
                addClickListener: false,
                activateMask: true);
        }

        [Test]
        public void Resolve_ExistingMaskWithImageAndButtonNoClick_ColorsAndClearsListeners()
        {
            var decision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab: true,
                hasExistingMask: true,
                hasImage: true,
                hasButton: true,
                hasClickAction: false);

            AssertDecision(
                decision,
                useMask: true,
                createMask: false,
                positionMask: true,
                applyColor: true,
                clearClickListeners: true,
                addClickListener: false,
                activateMask: true);
        }

        [Test]
        public void Resolve_ExistingMaskWithButtonAndClick_BindsClick()
        {
            var decision = UIManagerMaskActionPolicy.Resolve(
                hasMaskPrefab: true,
                hasExistingMask: true,
                hasImage: true,
                hasButton: true,
                hasClickAction: true);

            AssertDecision(
                decision,
                useMask: true,
                createMask: false,
                positionMask: true,
                applyColor: true,
                clearClickListeners: true,
                addClickListener: true,
                activateMask: true);
        }

        [Test]
        [Category("Boundary")]
        public void UIManager_ShowMask_DelegatesMaskActionGatesToPolicy()
        {
            var managerSource = File.ReadAllText(AssetPath("Runtime", "UI", "Core", "UIManager.cs"));
            var policyPath = AssetPath("Runtime", "UI", "Core", "Policies", "UIManagerMaskActionPolicy.cs");

            Assert.True(File.Exists(policyPath), "UIManager mask action gates must live outside UIManager.");

            var policySource = File.ReadAllText(policyPath);
            var showMask = SourceTextRegionExtractor.ExtractMethodRegion(
                managerSource,
                "private void ShowMask(UILayer layer, Color color, Action onClick, UIViewBase ownerView)");

            Assert.That(showMask, Does.Contain("var hasMaskPrefab = maskPrefab != null;"));
            Assert.That(showMask, Does.Contain("GameObject mask = null;"));
            Assert.That(showMask, Does.Contain("&& _maskInstances.TryGetValue(layer, out mask)"));
            Assert.That(showMask, Does.Contain("&& mask != null;"));
            Assert.That(showMask, Does.Contain("var createDecision = UIManagerMaskActionPolicy.Resolve("));
            Assert.That(showMask, Does.Contain("hasMaskPrefab: hasMaskPrefab"));
            Assert.That(showMask, Does.Contain("hasExistingMask: hasExistingMask"));
            Assert.That(showMask, Does.Contain("hasClickAction: onClick != null"));
            Assert.That(showMask, Does.Contain("if (!createDecision.UseMask)"));
            Assert.That(showMask, Does.Contain("if (createDecision.CreateMask)"));
            Assert.That(showMask, Does.Contain("mask = CreateRuntimeGameObject(maskPrefab, GetLayerContainer(layer));"));
            Assert.That(showMask, Does.Contain("_maskInstances[layer] = mask;"));
            Assert.That(showMask, Does.Contain("var image = mask.GetComponent<UnityEngine.UI.Image>();"));
            Assert.That(showMask, Does.Contain("var button = mask.GetComponent<UnityEngine.UI.Button>();"));
            Assert.That(showMask, Does.Contain("var actionDecision = UIManagerMaskActionPolicy.Resolve("));
            Assert.That(showMask, Does.Contain("hasExistingMask: true"));
            Assert.That(showMask, Does.Contain("hasImage: image != null"));
            Assert.That(showMask, Does.Contain("hasButton: button != null"));
            Assert.That(showMask, Does.Contain("if (actionDecision.PositionMask)"));
            Assert.That(showMask, Does.Contain("mask.transform.SetAsLastSibling();"));
            Assert.That(showMask, Does.Contain("ownerView.transform.parent == mask.transform.parent"));
            Assert.That(showMask, Does.Contain("mask.transform.SetSiblingIndex(ownerView.transform.GetSiblingIndex());"));
            Assert.That(showMask, Does.Contain("mask.transform.SetSiblingIndex(mask.transform.GetSiblingIndex() - 1);"));
            Assert.That(showMask, Does.Contain("if (actionDecision.ApplyColor)"));
            Assert.That(showMask, Does.Contain("image.color = color;"));
            Assert.That(showMask, Does.Contain("if (actionDecision.ClearClickListeners)"));
            Assert.That(showMask, Does.Contain("button.onClick.RemoveAllListeners();"));
            Assert.That(showMask, Does.Contain("if (actionDecision.AddClickListener)"));
            Assert.That(showMask, Does.Contain("button.onClick.AddListener(() => onClick());"));
            Assert.That(showMask, Does.Contain("if (actionDecision.ActivateMask)"));
            Assert.That(showMask, Does.Contain("mask.SetActive(true);"));
            Assert.That(showMask, Does.Not.Contain("if (maskPrefab == null) return;"));
            Assert.That(showMask, Does.Not.Contain("if (image != null) image.color = color;"));
            Assert.That(showMask, Does.Not.Contain("if (button != null)"));

            AssertOrder(
                showMask,
                "UIManagerMaskActionPolicy.Resolve(",
                "if (!createDecision.UseMask)",
                "Mask prefab availability must be classified before returning.");
            AssertOrder(
                showMask,
                "if (createDecision.CreateMask)",
                "var image = mask.GetComponent<UnityEngine.UI.Image>();",
                "Mask creation must happen before component reads.");
            AssertOrder(
                showMask,
                "var actionDecision = UIManagerMaskActionPolicy.Resolve(",
                "if (actionDecision.PositionMask)",
                "Component/action classification must happen before side effects.");
            AssertOrder(
                showMask,
                "if (actionDecision.PositionMask)",
                "if (actionDecision.ApplyColor)",
                "Mask positioning should stay before color application.");
            AssertOrder(
                showMask,
                "if (actionDecision.ClearClickListeners)",
                "if (actionDecision.AddClickListener)",
                "Existing click listeners must be cleared before adding a new click listener.");
            AssertOrder(
                showMask,
                "if (actionDecision.AddClickListener)",
                "if (actionDecision.ActivateMask)",
                "Click binding should stay before activation.");

            Assert.That(policySource, Does.Contain("public static UIManagerMaskActionDecision Resolve("));
            Assert.That(policySource, Does.Not.Contain("UnityEngine"));
            Assert.That(policySource, Does.Not.Contain("GameObject"));
            Assert.That(policySource, Does.Not.Contain("Transform"));
            Assert.That(policySource, Does.Not.Contain("UnityEngine.UI.Image"));
            Assert.That(policySource, Does.Not.Contain("UnityEngine.UI.Button"));
            Assert.That(policySource, Does.Not.Contain("_maskInstances"));
            Assert.That(policySource, Does.Not.Contain("maskPrefab"));
            Assert.That(policySource, Does.Not.Contain("CreateRuntimeGameObject"));
            Assert.That(policySource, Does.Not.Contain("SetActive"));
            Assert.That(policySource, Does.Not.Contain("onClick"));
        }

        private static void AssertDecision(
            UIManagerMaskActionDecision decision,
            bool useMask,
            bool createMask,
            bool positionMask,
            bool applyColor,
            bool clearClickListeners,
            bool addClickListener,
            bool activateMask)
        {
            Assert.That(decision.UseMask, Is.EqualTo(useMask));
            Assert.That(decision.CreateMask, Is.EqualTo(createMask));
            Assert.That(decision.PositionMask, Is.EqualTo(positionMask));
            Assert.That(decision.ApplyColor, Is.EqualTo(applyColor));
            Assert.That(decision.ClearClickListeners, Is.EqualTo(clearClickListeners));
            Assert.That(decision.AddClickListener, Is.EqualTo(addClickListener));
            Assert.That(decision.ActivateMask, Is.EqualTo(activateMask));
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


