using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ZeroEngine.UI.Tests.Runtime
{
    public sealed class UIManagerCachedModalOrderTests
    {
        private GameObject _managerRoot;
        private GameObject _templates;
        private GameObject _eventRoot;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_managerRoot != null) Object.Destroy(_managerRoot);
            if (_templates != null) Object.Destroy(_templates);
            if (_eventRoot != null) Object.Destroy(_eventRoot);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReopenCachedModal_OverLaterSameLayerView_RaisesViewAndMaskAndReceivesClick()
        {
            Assert.That(Object.FindObjectsByType<UIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty,
                "The fixture must not replace another UI owner.");
            _managerRoot = new GameObject("Cached modal ordering fixture", typeof(RectTransform));
            var manager = _managerRoot.AddComponent<UIManager>();
            _templates = new GameObject("Inactive synthetic UI templates", typeof(RectTransform));
            _templates.SetActive(false);
            var mask = new GameObject("SyntheticMask", typeof(RectTransform), typeof(Image), typeof(Button));
            mask.transform.SetParent(_templates.transform, false);
            var maskRect = mask.GetComponent<RectTransform>();
            maskRect.anchorMin = Vector2.zero;
            maskRect.anchorMax = Vector2.one;
            maskRect.sizeDelta = Vector2.zero;
            typeof(UIManager).GetField("maskPrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, mask);
            Register<CachedModalProbe>(manager);
            Register<LaterPanelProbe>(manager);

            var firstOpen = manager.OpenAsync<CachedModalProbe>();
            yield return Await(firstOpen);
            var original = firstOpen.Result;
            yield return Await(manager.CloseAsync(original));
            var panelOpen = manager.OpenAsync<LaterPanelProbe>();
            yield return Await(panelOpen);
            var panel = panelOpen.Result;
            var reopened = manager.OpenAsync<CachedModalProbe>();
            yield return Await(reopened);
            var modal = reopened.Result;
            Assert.That(modal, Is.SameAs(original), "This must exercise the cached instance.");
            Assert.That(modal.transform.parent, Is.SameAs(panel.transform.parent));
            Assert.That(modal.transform.GetSiblingIndex(), Is.GreaterThan(panel.transform.GetSiblingIndex()),
                "A reopened modal must be above a later-created view in the same layer.");
            var masks = (Dictionary<UILayer, GameObject>)typeof(UIManager)
                .GetField("_maskInstances", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            Assert.That(masks[UILayer.Popup].transform.GetSiblingIndex(), Is.EqualTo(modal.transform.GetSiblingIndex() - 1));
            Assert.That(masks[UILayer.Popup].transform.GetSiblingIndex(), Is.GreaterThan(panel.transform.GetSiblingIndex()));

            _eventRoot = new GameObject("Synthetic raycast event owner", typeof(EventSystem));
            var system = _eventRoot.GetComponent<EventSystem>();
            Canvas.ForceUpdateCanvases();
            yield return null;
            int clicks = 0;
            modal.GetComponent<Button>().onClick.AddListener(() => clicks++);
            var rect = modal.GetComponent<RectTransform>();
            var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(system) { position = position, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            system.RaycastAll(pointer, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject, Is.SameAs(modal.gameObject), "The cached modal must own the pointer hit.");
            ExecuteEvents.Execute(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Assert.That(clicks, Is.EqualTo(1));

            yield return Await(manager.CloseAsync(modal));
            Assert.That(panel.IsVisible, Is.True);
            Assert.That(masks[UILayer.Popup].transform.GetSiblingIndex(), Is.EqualTo(panel.transform.GetSiblingIndex() - 1));
        }

        private void Register<T>(UIManager manager) where T : UIViewBase
        {
            var prefab = new GameObject(typeof(T).Name, typeof(RectTransform), typeof(Image), typeof(Button));
            prefab.transform.SetParent(_templates.transform, false);
            prefab.AddComponent<T>();
            var rect = prefab.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(500, 250);
            manager.RegisterView(new UIViewConfig
            {
                viewName = typeof(T).Name, prefab = prefab, layer = UILayer.Popup,
                showMode = UIShowMode.Normal, showMask = true, cache = true,
                openAnimation = UIAnimationType.None, closeAnimation = UIAnimationType.None
            });
        }

        private static IEnumerator Await(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!task.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Synthetic UI operation did not settle.");
                yield return null;
            }
            if (task.IsFaulted) throw task.Exception.GetBaseException();
            Assert.That(task.IsCanceled, Is.False);
        }

        public sealed class CachedModalProbe : UIViewBase { }
        public sealed class LaterPanelProbe : UIViewBase { }
    }
}
