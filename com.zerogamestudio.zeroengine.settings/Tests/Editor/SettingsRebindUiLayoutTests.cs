using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.PlayerSettings.UI;

namespace ZeroEngine.PlayerSettings.Tests
{
    public sealed class SettingsRebindUiLayoutTests
    {
        [TestCase(690f, 708f)]
        [TestCase(560f, 600f)]
        [TestCase(960f, 720f)]
        public void DeviceTabsAndManyActions_StayInsideResponsiveShell(
            float width,
            float height)
        {
            RectTransform host = CreateHost(width, height);
            try
            {
                var builder = new SettingsRebindUiLayoutBuilder(host);
                SettingsRebindUiShell shell = builder.BuildShell(
                    "Rebind Controls",
                    "Choose a binding");
                builder.CreateDeviceTab(shell, "Keyboard Mouse Tab", "Keyboard & Mouse");
                builder.CreateDeviceTab(shell, "Gamepad Tab", "Gamepad");
                var rows = new List<SettingsRebindUiRow>();
                for (var index = 0; index < 40; index++)
                {
                    SettingsRebindUiRow row = builder.CreateBindingRow(
                        shell,
                        $"Action {index}",
                        $"Action {index}",
                        "Default");
                    row.BindingText.text = index == 0 ? "LB + X" : $"Key {index}";
                    rows.Add(row);
                }
                builder.CreateFooterButton(shell, "Reset All", "Restore Defaults", false);
                builder.CreateFooterButton(shell, "Done", "Done", true);

                SettingsRebindUiLayoutBuilder.Rebuild(shell, rows);

                Assert.That(shell.Root.rect.width, Is.EqualTo(width).Within(0.1f));
                Assert.That(shell.Root.rect.height, Is.EqualTo(height).Within(0.1f));
                Assert.That(shell.ScrollRect.vertical, Is.True);
                Assert.That(shell.Rows.rect.height, Is.GreaterThan(shell.Viewport.rect.height));
                Assert.That(
                    shell.Viewport.GetComponent<SettingsUiSelectionScroller>(),
                    Is.Not.Null);
                AssertContained(shell.Root, shell.DeviceTabs);
                AssertContained(shell.Root, shell.Viewport);
                AssertContained(shell.Root, shell.Footer);
                AssertSeparated(shell.Footer, shell.Viewport);
                AssertContained(rows[0].Root, rows[0].BindingButton.transform as RectTransform);
                AssertContained(rows[0].Root, rows[0].ResetButton.transform as RectTransform);
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        [Test]
        public void SelectionScroller_BringsLastActionIntoViewport()
        {
            RectTransform host = CreateHost(560f, 600f);
            try
            {
                var builder = new SettingsRebindUiLayoutBuilder(host);
                SettingsRebindUiShell shell = builder.BuildShell("Bindings", "Choose");
                builder.CreateDeviceTab(shell, "Keyboard Mouse Tab", "Keyboard & Mouse");
                builder.CreateDeviceTab(shell, "Gamepad Tab", "Gamepad");
                var rows = new List<SettingsRebindUiRow>();
                for (var index = 0; index < 40; index++)
                {
                    rows.Add(builder.CreateBindingRow(
                        shell,
                        $"Action {index}",
                        $"Action {index}",
                        "Default"));
                }
                SettingsRebindUiLayoutBuilder.Rebuild(shell, rows);

                SettingsUiSelectionScroller scroller =
                    shell.Viewport.GetComponent<SettingsUiSelectionScroller>();
                scroller.EnsureVisible((RectTransform)rows[^1].BindingButton.transform);

                Rect viewport = WorldRect(shell.Viewport);
                Rect selected = WorldRect((RectTransform)rows[^1].BindingButton.transform);
                Assert.That(selected.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - 0.1f));
                Assert.That(selected.yMax, Is.LessThanOrEqualTo(viewport.yMax + 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static RectTransform CreateHost(float width, float height)
        {
            var gameObject = new GameObject("Rebind Host", typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void AssertContained(RectTransform container, RectTransform child)
        {
            Assert.That(child, Is.Not.Null);
            Rect outer = WorldRect(container);
            Rect inner = WorldRect(child);
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 0.1f));
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 0.1f));
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 0.1f));
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 0.1f));
        }

        private static void AssertSeparated(RectTransform fixedArea, RectTransform content)
        {
            Rect fixedRect = WorldRect(fixedArea);
            Rect contentRect = WorldRect(content);
            Assert.That(
                fixedRect.yMax <= contentRect.yMin + 0.1f
                || contentRect.yMax <= fixedRect.yMin + 0.1f,
                Is.True);
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
