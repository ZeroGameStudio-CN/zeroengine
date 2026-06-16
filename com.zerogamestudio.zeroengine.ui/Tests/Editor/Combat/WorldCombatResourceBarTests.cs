using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.UI.Combat;

namespace ZeroEngine.UI.Tests.Editor.Combat
{
    public sealed class WorldCombatResourceBarTests
    {
        [Test]
        public void SetValue_CompatibilityOverload_UpdatesFrontDelayedAndShieldWithoutThirdPartyDependencies()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var delayed = CreateFill(root.transform, "Delayed");
                var shield = CreateFill(root.transform, "Shield");
                var bar = root.AddComponent<WorldCombatResourceBar>();

                bar.ConfigureForRuntime(front, delayed, shield);
                bar.SetValue(40f, 100f, instant: true);
                bar.SetShield(25f, 100f);

                Assert.That(bar.ValueNormalized, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(bar.DelayedNormalized, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(bar.ShieldNormalized, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(front.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(delayed.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
                Assert.True(shield.gameObject.activeSelf);

                bar.SetValue(10f, 100f);
                Assert.That(front.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(delayed.fillAmount, Is.EqualTo(0.4f).Within(0.001f));

                bar.AdvanceFront(10f);
                Assert.That(front.fillAmount, Is.EqualTo(0.1f).Within(0.001f));

                bar.AdvanceDelayed(10f);
                Assert.That(delayed.fillAmount, Is.EqualTo(0.1f).Within(0.001f));

                bar.SetShield(0f, 100f);
                Assert.False(shield.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetValue_RawValueChangesWithTinyPercentageDelta_StillRefreshesTextAndState()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var delayed = CreateFill(root.transform, "Delayed");
                var valueText = CreateText(root.transform, "ValueText");
                var bar = root.AddComponent<CombatResourceBar>();
                var style = ScriptableObject.CreateInstance<CombatResourceBarStyle>();
                style.VisibilityMode = ResourceBarVisibilityMode.AlwaysVisible;
                style.ShowValueText = true;

                bar.ConfigureForRuntime(front, delayed, null, valueText, null);
                bar.ApplyStyle(style);
                bar.SetValue(1000000f, 0f, 1000000f, instant: true);
                bar.SetValue(999999f, 0f, 1000000f);

                Assert.That(bar.ValueNormalized, Is.LessThan(1f));
                Assert.That(valueText.text, Is.EqualTo("999999/1000000"));
                Assert.True(root.activeSelf);

                Object.DestroyImmediate(style);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetValue_Damage_HoldsFrontAndDelayedThenCatchesUp()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var delayed = CreateFill(root.transform, "Delayed");
                var bar = root.AddComponent<CombatResourceBar>();

                bar.ConfigureForRuntime(front, delayed, null, null, null);
                bar.SetValue(80f, 0f, 100f, instant: true);
                bar.SetValue(20f, 0f, 100f);

                Assert.That(front.fillAmount, Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(delayed.fillAmount, Is.EqualTo(0.8f).Within(0.001f));

                bar.AdvanceFront(10f);
                Assert.That(front.fillAmount, Is.EqualTo(0.2f).Within(0.001f));

                bar.AdvanceDelayed(10f);
                Assert.That(delayed.fillAmount, Is.EqualTo(0.2f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetShield_DrawsShieldAsSegmentAfterCurrentValue()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var delayed = CreateFill(root.transform, "Delayed");
                var shield = CreateFill(root.transform, "Shield");
                var bar = root.AddComponent<WorldCombatResourceBar>();

                bar.ConfigureForRuntime(front, delayed, shield);
                bar.SetValue(40f, 100f, instant: true);
                bar.SetShield(25f, 100f);

                var shieldRect = shield.rectTransform;
                Assert.That(shield.fillAmount, Is.EqualTo(1f).Within(0.001f));
                Assert.That(shieldRect.anchorMin.x, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(shieldRect.anchorMax.x, Is.EqualTo(0.65f).Within(0.001f));
                Assert.That(shieldRect.offsetMin.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(shieldRect.offsetMax.x, Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetShield_RawShieldChangesWithTinyPercentageDelta_StillRefreshesSegment()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var shield = CreateFill(root.transform, "Shield");
                var bar = root.AddComponent<CombatResourceBar>();

                bar.ConfigureForRuntime(front, null, shield, null, null);
                bar.SetValue(500000f, 0f, 1000000f, instant: true);
                bar.SetShield(1f, 1000000f);

                Assert.True(shield.gameObject.activeSelf);
                Assert.That(shield.rectTransform.anchorMin.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(shield.rectTransform.anchorMax.x, Is.GreaterThan(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetShield_Overflow_ClampsEndWithoutMovingHealthStart()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var shield = CreateFill(root.transform, "Shield");
                var bar = root.AddComponent<CombatResourceBar>();

                bar.ConfigureForRuntime(front, null, shield, null, null);
                bar.SetValue(90f, 0f, 100f, instant: true);
                bar.SetShield(25f, 100f);

                Assert.That(front.fillAmount, Is.EqualTo(0.9f).Within(0.001f));
                Assert.That(shield.rectTransform.anchorMin.x, Is.EqualTo(0.9f).Within(0.001f));
                Assert.That(shield.rectTransform.anchorMax.x, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void VisibilityMode_AlwaysVisible_RemainsVisibleAtFullValue()
        {
            var root = new GameObject("ResourceBar", typeof(RectTransform));
            try
            {
                var front = CreateFill(root.transform, "Front");
                var bar = root.AddComponent<CombatResourceBar>();
                var style = ScriptableObject.CreateInstance<CombatResourceBarStyle>();
                style.VisibilityMode = ResourceBarVisibilityMode.AlwaysVisible;

                bar.ConfigureForRuntime(front, null, null, null, null);
                bar.ApplyStyle(style);
                bar.SetValue(100f, 0f, 100f, instant: true);

                Assert.True(root.activeSelf);

                Object.DestroyImmediate(style);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldStatus_HideWhenFull_HidesOnlyHealthBarRoot()
        {
            var root = new GameObject("Status", typeof(RectTransform));
            try
            {
                var status = root.AddComponent<WorldCombatStatusView>();
                var healthRoot = new GameObject("HealthRoot", typeof(RectTransform));
                healthRoot.transform.SetParent(root.transform, false);
                var faction = CreateText(root.transform, "Faction");
                var name = CreateText(root.transform, "Name");
                var value = CreateText(root.transform, "Value");
                var strip = CreateFill(root.transform, "Strip");
                var health = CreateFill(healthRoot.transform, "Health");
                var delayed = CreateFill(healthRoot.transform, "Delayed");
                var shield = CreateFill(healthRoot.transform, "Shield");
                var selected = new GameObject("Selected");
                selected.transform.SetParent(root.transform, false);
                var turn = new GameObject("Turn");
                turn.transform.SetParent(root.transform, false);
                var style = ScriptableObject.CreateInstance<CombatResourceBarStyle>();
                style.VisibilityMode = ResourceBarVisibilityMode.HideWhenFull;

                status.ConfigureForRuntime(faction, name, value, strip, health, delayed, shield, selected, turn);
                status.ApplyHealthBarStyle(style);
                status.SetHealth(100f, 100f, instant: true);

                Assert.True(root.activeSelf, "Hiding a world health bar must not hide the whole status view.");
                Assert.True(name.gameObject.activeSelf, "Name text must remain visible when only the health bar is hidden.");
                Assert.False(healthRoot.activeSelf, "Only the health bar root should hide at full health.");

                Object.DestroyImmediate(style);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldStatus_WithResourceBar_ManualAdvanceDelegatesToResourceBarOnce()
        {
            var root = new GameObject("Status", typeof(RectTransform));
            try
            {
                var status = root.AddComponent<WorldCombatStatusView>();
                var health = CreateFill(root.transform, "Health");
                var delayed = CreateFill(root.transform, "Delayed");
                var shield = CreateFill(root.transform, "Shield");

                status.ConfigureForRuntime(null, null, null, null, health, delayed, shield, null, null);
                status.SetHealth(80f, 100f, instant: true);
                status.SetHealth(20f, 100f);

                status.AdvanceDelayedHealth(0.1f);

                Assert.That(delayed.fillAmount, Is.EqualTo(0.64f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldStatus_SetShieldAfterDamage_DoesNotSyncDelayedFill()
        {
            var root = new GameObject("Status", typeof(RectTransform));
            try
            {
                var status = root.AddComponent<WorldCombatStatusView>();
                var health = CreateFill(root.transform, "Health");
                var delayed = CreateFill(root.transform, "Delayed");
                var shield = CreateFill(root.transform, "Shield");

                status.ConfigureForRuntime(null, null, null, null, health, delayed, shield, null, null);
                status.SetHealth(80f, 100f, instant: true);
                status.SetHealth(20f, 100f);
                status.SetShield(2f, 4f);

                Assert.That(delayed.fillAmount, Is.EqualTo(0.8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void WorldStatus_Update_LeavesResourceBarDelayedFillToResourceBar()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WorldCombatStatusView).Assembly);
            Assert.NotNull(packageInfo);
            var source = System.IO.File.ReadAllText(
                System.IO.Path.Combine(packageInfo.resolvedPath, "Runtime/UI/Combat/WorldCombatStatusView.cs")).Replace("\r\n", "\n");

            Assert.That(source, Does.Contain("private void Update()\n        {\n            if (_healthBar == null)\n            {\n                AdvanceDelayedHealth(Time.unscaledDeltaTime);\n            }\n        }"));
        }

        [Test]
        public void RuntimeAssembly_DoesNotReferenceProjectOrFeedbackPlugins()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CombatResourceBar).Assembly);
            Assert.NotNull(packageInfo);
            var asmdef = System.IO.File.ReadAllText(
                System.IO.Path.Combine(packageInfo.resolvedPath, "Runtime/ZeroEngine.UI.asmdef"));

            Assert.That(asmdef, Does.Not.Contain("DamageNumbersPro"));
            Assert.That(asmdef, Does.Not.Contain("DOTween"));
            Assert.That(asmdef, Does.Not.Contain("MoreMountains"));
            Assert.That(asmdef, Does.Not.Contain("ZGS."));
            Assert.That(asmdef, Does.Not.Contain("POB"));
        }

        private static Image CreateFill(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }
    }
}
