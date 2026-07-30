using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.PlayerSettings.UI;

namespace ZeroEngine.PlayerSettings.Tests
{
    public sealed class SettingsUiLayoutTests
    {
        [TestCase(690f, 708f)]
        [TestCase(560f, 600f)]
        [TestCase(960f, 720f)]
        public void FallbackLayout_KeepsShellAndColumnsSeparated(float width, float height)
        {
            RectTransform host = CreateHost(width, height);
            try
            {
                var builder = new SettingsUiLayoutBuilder(host);
                SettingsUiShell shell = builder.BuildShell("Settings", "Changes preview immediately");
                builder.CreateTab(shell, "Controls Tab", "Controls");
                builder.CreateTab(shell, "Display Tab", "Display");
                builder.CreateTab(shell, "Audio Tab", "Audio");
                builder.CreateTab(shell, "Accessibility Tab", "Accessibility");
                SettingsUiCategoryView category = builder.CreateCategory(shell, "Controls");
                builder.CreateSliderRow(category, "Pointer", "Pointer sensitivity", out _);
                builder.CreateSliderRow(category, "Gamepad", "Gamepad sensitivity", out _);
                builder.CreateSliderRow(category, "Deadzone", "Stick deadzone", out _);
                builder.CreateToggleRow(category, "Invert Y", "Invert vertical look");
                builder.CreateToggleRow(category, "Precision", "Precision aim toggle");
                builder.CreateChoiceRow(category, "Glyphs", "Gamepad glyphs", out _);
                builder.CreateActionRow(category, "Rebind", "Bindings", "Rebind");
                builder.CreateFooterButton(shell, "Reset", "Restore defaults", false);
                builder.CreateFooterButton(shell, "Save", "Save and back", true);

                SettingsUiLayoutBuilder.Rebuild(shell, category);

                Assert.That(shell.Root.rect.width, Is.EqualTo(width).Within(0.1f));
                Assert.That(shell.Root.rect.height, Is.EqualTo(height).Within(0.1f));
                AssertSeparated(shell.TabBar, shell.Content);
                AssertSeparated(shell.Footer, shell.Content);
                Assert.That(category.ScrollRect.viewport, Is.SameAs(category.Root.transform));
                Assert.That(category.Content.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
                Assert.That(category.Content.GetComponent<ContentSizeFitter>(), Is.Not.Null);
                Assert.That(category.Selectables.Count, Is.EqualTo(7));
                AssertSliderColumns(category.Content.Find("Pointer Row"));
                AssertChoiceColumns(category.Content.Find("Glyphs Row"));
                AssertContained(
                    (RectTransform)category.Content.Find("Invert Y Row"),
                    (RectTransform)category.Content.Find("Invert Y Row/Invert Y"));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        [Test]
        public void SmallHost_UsesScrollInsteadOfMovingFooter()
        {
            RectTransform host = CreateHost(560f, 600f);
            try
            {
                var builder = new SettingsUiLayoutBuilder(host);
                SettingsUiShell shell = builder.BuildShell("Settings", "Fallback");
                SettingsUiCategoryView category = builder.CreateCategory(shell, "Long Category");
                for (var i = 0; i < 10; i++)
                {
                    builder.CreateSliderRow(category, $"Slider {i}", $"Setting {i}", out _);
                }
                builder.CreateFooterButton(shell, "Save", "Save", true);

                SettingsUiLayoutBuilder.Rebuild(shell, category);

                Assert.That(category.Content.rect.height, Is.GreaterThan(shell.Content.rect.height));
                Assert.That(category.ScrollRect.vertical, Is.True);
                AssertSeparated(shell.Footer, shell.Content);
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static RectTransform CreateHost(float width, float height)
        {
            var gameObject = new GameObject("Settings Host", typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void AssertSliderColumns(Transform row)
        {
            Assert.That(row, Is.Not.Null);
            RectTransform label = (RectTransform)row.Find("Pointer Label");
            RectTransform control = (RectTransform)row.Find("Pointer");
            RectTransform value = (RectTransform)row.Find("Pointer Value");
            Assert.That(label.anchorMax.x, Is.LessThan(control.anchorMin.x));
            Assert.That(control.anchorMax.x, Is.LessThan(value.anchorMin.x));
        }

        private static void AssertChoiceColumns(Transform row)
        {
            Assert.That(row, Is.Not.Null);
            RectTransform label = (RectTransform)row.Find("Glyphs Label");
            RectTransform host = (RectTransform)row.Find("Glyphs Host");
            Assert.That(label.anchorMax.x, Is.LessThan(host.anchorMin.x));
        }

        private static void AssertSeparated(RectTransform fixedArea, RectTransform content)
        {
            Rect fixedRect = WorldRect(fixedArea);
            Rect contentRect = WorldRect(content);
            bool separated = fixedRect.yMax <= contentRect.yMin + 0.1f
                             || contentRect.yMax <= fixedRect.yMin + 0.1f;
            Assert.That(separated, Is.True, $"{fixedArea.name} overlaps {content.name}.");
        }

        private static void AssertContained(RectTransform container, RectTransform child)
        {
            Rect outer = WorldRect(container);
            Rect inner = WorldRect(child);
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 0.1f));
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 0.1f));
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 0.1f));
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 0.1f));
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
