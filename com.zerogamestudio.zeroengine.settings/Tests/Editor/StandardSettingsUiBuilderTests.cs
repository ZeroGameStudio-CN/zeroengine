using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.PlayerSettings.UI;

namespace ZeroEngine.PlayerSettings.Tests
{
    public sealed class StandardSettingsUiBuilderTests
    {
        [TestCase(690f, 708f)]
        [TestCase(560f, 600f)]
        [TestCase(960f, 720f)]
        public void Build_CreatesCompleteStandardBaseline(float width, float height)
        {
            RectTransform host = CreateHost(width, height);
            try
            {
                StandardSettingsUiView view =
                    new StandardSettingsUiBuilder(host).Build(
                        StandardSettingsUiText.SimplifiedChinese);
                SettingId[] expected = StandardSettingsCatalog.Create(
                        new StandardSettingsDefaults(),
                        () => new[] { "zh-CN", "en-US" },
                        () => new[] { "Low", "High" })
                    .Select(definition => definition.Id)
                    .ToArray();

                CollectionAssert.AreEquivalent(expected, view.Controls.Keys);
                Assert.That(view.Controls.Count, Is.EqualTo(expected.Length));
                Assert.That(
                    view.Choice(StandardSettingIds.Width),
                    Is.SameAs(view.Choice(StandardSettingIds.Height)));
                Assert.That(
                    view.Category(StandardSettingsUiCategory.Controls).Selectables.Count,
                    Is.EqualTo(7));
                Assert.That(
                    view.Category(StandardSettingsUiCategory.Display).Selectables.Count,
                    Is.EqualTo(6));
                Assert.That(
                    view.Category(StandardSettingsUiCategory.Audio).Selectables.Count,
                    Is.EqualTo(3));
                Assert.That(
                    view.Category(StandardSettingsUiCategory.Accessibility).Selectables.Count,
                    Is.EqualTo(4));
                Assert.That(
                    view.Slider(StandardSettingIds.FrameRateLimit).wholeNumbers,
                    Is.True);
                Assert.That(
                    view.Category(StandardSettingsUiCategory.Controls).Content.rect.height,
                    Is.GreaterThan(0f));
                Assert.That(view.Shell.Root.rect.width, Is.EqualTo(width).Within(0.1f));
                Assert.That(view.Shell.Root.rect.height, Is.EqualTo(height).Within(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        [Test]
        public void ApplyText_UpdatesLanguageWithoutChangingBaseline()
        {
            RectTransform host = CreateHost(690f, 708f);
            try
            {
                StandardSettingsUiView view =
                    new StandardSettingsUiBuilder(host).Build(
                        StandardSettingsUiText.SimplifiedChinese);
                int controlCount = view.Controls.Count;

                view.ApplyText(StandardSettingsUiText.English);
                view.ShowCategory(StandardSettingsUiCategory.Accessibility);

                Assert.That(
                    view.Shell.Root.Find("Header/Title").GetComponent<Text>().text,
                    Is.EqualTo("Settings"));
                Assert.That(
                    view.Tab(StandardSettingsUiCategory.Accessibility)
                        .GetComponentInChildren<Text>().text,
                    Is.EqualTo("◆ Accessibility"));
                Assert.That(
                    view.Category(StandardSettingsUiCategory.Accessibility)
                        .Content.Find("Language Row/Language Label")
                        .GetComponent<Text>().text,
                    Is.EqualTo("Language"));
                Assert.That(view.Controls.Count, Is.EqualTo(controlCount));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        [Test]
        public void ProjectRows_CanOnlyAppendToStandardCategory()
        {
            RectTransform host = CreateHost(690f, 708f);
            try
            {
                StandardSettingsUiView view =
                    new StandardSettingsUiBuilder(host).Build();
                SettingsUiCategoryView display =
                    view.Category(StandardSettingsUiCategory.Display);
                int standardCount = display.Selectables.Count;

                view.Layout.CreateSliderRow(
                    display,
                    "Project Field Of View",
                    "Field Of View",
                    out _);
                view.Rebuild();

                Assert.That(display.Selectables.Count, Is.EqualTo(standardCount + 1));
                Assert.That(view.Controls.Count, Is.EqualTo(21));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static RectTransform CreateHost(float width, float height)
        {
            var gameObject = new GameObject("Standard Settings Host", typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
    }
}
