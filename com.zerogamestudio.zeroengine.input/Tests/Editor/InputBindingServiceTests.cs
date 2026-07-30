using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem.Tests
{
    public sealed class InputBindingServiceTests
    {
        private InputActionAsset _asset;
        private InputActionMap _map;
        private InputAction _jump;
        private InputAction _use;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _map = new InputActionMap("Player");
            _asset.AddActionMap(_map);
            _jump = _map.AddAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space", groups: "KeyboardMouse");
            _use = _map.AddAction("Use", InputActionType.Button);
            _use.AddBinding("<Keyboard>/e", groups: "KeyboardMouse");
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_asset);

        [Test]
        public void ApplyOverride_UsesActionAndBindingGuids()
        {
            var service = new InputBindingService(_asset);

            var result = service.TryApplyOverride(_jump.id, _jump.bindings[0].id, "<Keyboard>/q");

            Assert.That(result.Success, Is.True);
            Assert.That(_jump.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/q"));
            Assert.That(
                service.GetEffectivePath(_jump.id, _jump.bindings[0].id),
                Is.EqualTo("<Keyboard>/q"));
            Assert.That(
                service.GetBindingDisplayString(_jump.id, _jump.bindings[0].id),
                Is.Not.Empty);
        }

        [Test]
        public void ConflictSwap_ExchangesPathsWithinBindingGroup()
        {
            var service = new InputBindingService(_asset);

            var result = service.TryApplyOverride(
                _jump.id, _jump.bindings[0].id, "<Keyboard>/e", InputBindingConflictPolicy.Swap);

            Assert.That(result.Success, Is.True);
            Assert.That(_jump.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/e"));
            Assert.That(_use.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/space"));
        }

        [Test]
        public void ConflictReject_LeavesBothBindingsUnchanged()
        {
            var service = new InputBindingService(_asset);

            var result = service.TryApplyOverride(
                _jump.id, _jump.bindings[0].id, "<Keyboard>/e", InputBindingConflictPolicy.Reject);

            Assert.That(result.Status, Is.EqualTo(InputRebindStatus.ConflictRejected));
            Assert.That(_jump.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/space"));
            Assert.That(_use.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/e"));
        }

        [TestCase("<Mouse>/delta")]
        [TestCase("<Mouse>/position")]
        [TestCase("<Touchscreen>/primaryTouch/position")]
        public void ContinuousPointerControls_AreRejected(string path)
        {
            var result = new InputBindingService(_asset)
                .TryApplyOverride(_jump.id, _jump.bindings[0].id, path);

            Assert.That(result.Status, Is.EqualTo(InputRebindStatus.IncompatibleControl));
        }

        [Test]
        public void Overrides_RoundTripAsWholeAssetJson()
        {
            var service = new InputBindingService(_asset);
            service.TryApplyOverride(_jump.id, _jump.bindings[0].id, "<Keyboard>/q");
            var json = service.SaveOverrides();
            service.ResetAll();

            Assert.That(service.TryLoadOverrides(json), Is.True);
            Assert.That(_jump.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/q"));
        }

        [Test]
        public void CompositeParts_DisplayRebindResetAndRoundTripByGuid()
        {
            InputAction combo = _map.AddAction("Combo", InputActionType.Button);
            combo.AddCompositeBinding("OneModifier")
                .With("Modifier", "<Gamepad>/leftShoulder", groups: "Gamepad")
                .With("Binding", "<Gamepad>/buttonWest", groups: "Gamepad");
            combo.AddBinding("<Gamepad>/buttonNorth", groups: "Gamepad");
            Guid modifierId = combo.bindings[1].id;
            Guid buttonId = combo.bindings[2].id;
            var parts = new[] { modifierId, buttonId };
            var service = new InputBindingService(_asset);

            string initialDisplay = service.GetBindingDisplayString(combo.id, parts);
            Assert.That(
                initialDisplay,
                Is.EqualTo(
                    service.GetBindingDisplayString(combo.id, modifierId)
                    + " + "
                    + service.GetBindingDisplayString(combo.id, buttonId)));

            Assert.That(
                service.TryApplyOverride(
                    combo.id,
                    modifierId,
                    "<Gamepad>/rightShoulder",
                    InputBindingConflictPolicy.Allow).Success,
                Is.True);
            Assert.That(
                service.TryApplyOverride(
                    combo.id,
                    buttonId,
                    "<Gamepad>/buttonSouth",
                    InputBindingConflictPolicy.Allow).Success,
                Is.True);
            string json = service.SaveOverrides();
            service.ResetAll();
            Assert.That(service.TryLoadOverrides(json), Is.True);
            Assert.That(
                combo.bindings[1].effectivePath,
                Is.EqualTo("<Gamepad>/rightShoulder"));
            Assert.That(
                combo.bindings[2].effectivePath,
                Is.EqualTo("<Gamepad>/buttonSouth"));
            Assert.That(service.ResetBindings(combo.id, parts), Is.True);
            Assert.That(
                combo.bindings[1].effectivePath,
                Is.EqualTo("<Gamepad>/leftShoulder"));
            Assert.That(
                combo.bindings[2].effectivePath,
                Is.EqualTo("<Gamepad>/buttonWest"));
            Assert.That(
                combo.bindings[3].effectivePath,
                Is.EqualTo("<Gamepad>/buttonNorth"),
                "The action keeps a single-button alternative binding.");
        }

        [Test]
        public void PassiveMouseDelta_IsNotDeliberatePointerIntent()
        {
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();
            try
            {
                Assert.That(InputDevicePresentationTracker.IsDeliberatePointerIntent(mouse.delta), Is.False);
                Assert.That(InputDevicePresentationTracker.IsDeliberatePointerIntent(mouse.leftButton), Is.True);
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            }
        }
    }
}
