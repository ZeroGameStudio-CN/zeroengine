using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.UI.Tests
{
    public sealed class DirectionalFocusNavigationTests
    {
        private RectTransform root;

        [SetUp]
        public void SetUp() => root = Rect("Panel", null, Vector2.zero);

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root.gameObject);

        [Test]
        public void SearchesWithinPanelAndHonorsFocusablePreferredTarget()
        {
            var current = Button("Current", root, Vector2.zero);
            var sameColumn = Button("Same column", root, Vector2.down * 110);
            var diagonal = Button("Diagonal", root, new Vector2(65, -60));
            var excluded = Button("Pointer only", root, Vector2.down * 45);
            excluded.navigation = new Navigation { mode = Navigation.Mode.None };
            var disabled = Button("Disabled", root, Vector2.down * 20);
            disabled.enabled = false;
            var otherRoot = Rect("Other panel", null, Vector2.zero);
            try
            {
                var outside = Button("Foreign", otherRoot, Vector2.down * 30);
                var navigation = new DirectionalFocusNavigation(root);
                Assert.That(navigation.FindNext(current, MoveDirection.Down, outside), Is.SameAs(sameColumn));
                Assert.That(navigation.FindNext(current, MoveDirection.Down, diagonal), Is.SameAs(diagonal));
                Assert.That(navigation.FindNext(current, MoveDirection.Down, excluded), Is.SameAs(sameColumn));
            }
            finally { Object.DestroyImmediate(otherRoot.gameObject); }
        }

        [Test]
        public void WrapIsScopedAndOptional()
        {
            var first = Button("First", root, Vector2.zero);
            var last = Button("Last", root, Vector2.down * 115);
            Assert.That(new DirectionalFocusNavigation(root).FindNext(last, MoveDirection.Down), Is.Null);
            Assert.That(new DirectionalFocusNavigation(root, wrapAround: true).FindNext(last, MoveDirection.Down),
                Is.SameAs(first));
        }

        [Test]
        public void ReturningToScrollContentRestoresOnlyAnEligibleRow()
        {
            var previousTab = Button("Tab", root, Vector2.up * 75);
            var scroll = Rect("List", root, Vector2.zero).gameObject.AddComponent<ScrollRect>();
            scroll.content = Rect("Content", (RectTransform)scroll.transform, Vector2.zero);
            var first = Button("First", scroll.content, Vector2.zero);
            var remembered = Button("Remembered", scroll.content, Vector2.down * 50);
            var navigation = new DirectionalFocusNavigation(root);

            navigation.RecordMove(remembered, previousTab);
            Assert.That(navigation.FindNext(previousTab, MoveDirection.Down), Is.SameAs(remembered));
            remembered.interactable = false;
            Assert.That(navigation.FindNext(previousTab, MoveDirection.Down), Is.SameAs(first));
            remembered.interactable = true;
            Assert.That(navigation.FindNext(previousTab, MoveDirection.Down), Is.SameAs(first));
            navigation.RecordMove(remembered, previousTab);
            scroll.enabled = false;
            Assert.That(navigation.FindNext(previousTab, MoveDirection.Down), Is.SameAs(first));
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
            rect.sizeDelta = new Vector2(80, 32);
            rect.anchoredPosition = position;
            return rect;
        }

        private static Button Button(string name, Transform parent, Vector2 position) =>
            Rect(name, parent, position).gameObject.AddComponent<Button>();
    }
}
