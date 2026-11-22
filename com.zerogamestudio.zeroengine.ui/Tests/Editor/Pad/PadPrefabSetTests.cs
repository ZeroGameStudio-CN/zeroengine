using System;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZeroEngine.UI.Pad;
using Object = UnityEngine.Object;

namespace ZeroEngine.UI.Tests.Editor
{
    public sealed class PadPrefabSetTests
    {
        private GameObject _root;
        private PadPrefabSet _set;
        private Scene _testScene;

        [SetUp]
        public void SetUp()
        {
            _testScene = EditorSceneManager.NewPreviewScene();
            _root = EditorUtility.CreateGameObjectWithHideFlags("PAD Test", HideFlags.HideAndDontSave, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(_root, _testScene);
            _root.SetActive(false);
            _set = ScriptableObject.CreateInstance<PadPrefabSet>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_set);
            Object.DestroyImmediate(_root);
            if (_testScene.IsValid())
                EditorSceneManager.ClosePreviewScene(_testScene);
        }

        [Test]
        public void PagesShareTemplatesAndOwnTheirInstances()
        {
            var panelTemplate = Child<PadPanelView>("Panel", _root.transform);
            var title = Child<TextMeshProUGUI>("Title", panelTemplate.transform);
            title.text = string.Empty;
            var content = Child<RectTransform>("Content", panelTemplate.transform);
            var close = Child<Button>("Close", panelTemplate.transform);
            panelTemplate.ConfigureForRuntime(title, close, content);
            var row = Child<Button>("Row", _root.transform);
            var primary = Child<Button>("Action", _root.transform);
            var metricTemplate = BuildMetric(_root.transform);
            _set.ConfigureForRuntime(panelTemplate, row, primary, metricTemplate);

            var history = _set.CreatePanel(_root.transform);
            var relic = _set.CreatePanel(_root.transform);
            history.SetTitle("历史记录");
            relic.SetTitle("遗物");

            Assert.That(history.Content, Is.Not.SameAs(relic.Content));
            Assert.That(history.CloseButton, Is.Not.SameAs(relic.CloseButton));
            Assert.That(_set.CreateNavigationRow(history.Content).transform.parent, Is.SameAs(history.Content));
            Assert.That(_set.CreatePrimaryAction(relic.Content).transform.parent, Is.SameAs(relic.Content));
            Assert.That(_set.CreateMetric(history.Content).HasRequiredReferences, Is.True);
            Assert.That(history.GetComponentInChildren<TextMeshProUGUI>(true).text, Is.EqualTo("历史记录"));
            Assert.That(relic.GetComponentInChildren<TextMeshProUGUI>(true).text, Is.EqualTo("遗物"));
            Assert.That(title.text, Is.Empty);
        }

        [Test]
        public void MetricKeepsTheLabelVisibleAndValuesOnOneLine()
        {
            var metric = BuildMetric(_root.transform);
            metric.SetContent(null, "击杀数", "2.15B");
            var caption = metric.transform.Find("Label").GetComponent<TMP_Text>();
            var value = metric.transform.Find("Value").GetComponent<TMP_Text>();

            Assert.That(caption.text, Is.EqualTo("击杀数"));
            Assert.That(caption.enabled, Is.True);
            Assert.That(value.text, Is.EqualTo("2.15B"));
            foreach (var text in new[] { caption, value })
            {
                Assert.That(text.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));
                Assert.That(text.raycastTarget, Is.False);
            }
        }

        [Test]
        public void MissingTemplatesFailBeforeAddingAnyChildren()
        {
            Assert.Throws<InvalidOperationException>(() => _set.CreatePanel(_root.transform));
            Assert.Throws<InvalidOperationException>(() => _set.CreateMetric(_root.transform));
            Assert.Throws<InvalidOperationException>(() => _set.CreateNavigationRow(_root.transform));
            Assert.That(_root.transform.childCount, Is.Zero);
        }

        private static PadMetricView BuildMetric(Transform parent)
        {
            var metric = Child<PadMetricView>("Metric", parent);
            metric.ConfigureForRuntime(Child<Image>("Icon", metric.transform),
                Child<TextMeshProUGUI>("Label", metric.transform), Child<TextMeshProUGUI>("Value", metric.transform));
            return metric;
        }

        private static T Child<T>(string name, Transform parent) where T : Component
        {
            var child = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return typeof(T) == typeof(RectTransform) ? child.GetComponent<T>() : child.AddComponent<T>();
        }
    }
}
