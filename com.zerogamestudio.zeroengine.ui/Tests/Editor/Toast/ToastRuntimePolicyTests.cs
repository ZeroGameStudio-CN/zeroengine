using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.UI.Toast;
using ToastApi = ZeroEngine.UI.Toast.Toast;

namespace ZeroEngine.UI.Tests.Editor.Toast
{
    public sealed class ToastRuntimePolicyTests
    {
        public static void RunBatchVerification()
        {
            var tests = new ToastRuntimePolicyTests();
            tests.ToastSettings_DefaultsAreUsableForAnyGame();
            tests.Resolver_UsesTextKeyBeforeFallbackMessage();
            tests.Manager_RefreshesDuplicateWhenPolicySaysRefresh();
            tests.Manager_DropsOldestWhenVisibleLimitIsReached();
            tests.Manager_QueuePolicyDropsIncomingWhenQueueLimitIsZero();
            tests.Manager_ClearGroupRemovesQueuedRequests();
            tests.Manager_RepositionsToastsPerAnchor();
            tests.Manager_BottomAnchorsStackUpward();
            tests.Manager_UsesOverrideColorForAccentOnly();
            tests.RootPresenter_DisableClearsRuntimeState();
            tests.Manager_ClearGroupOnlyClearsMatchingGroup();
            tests.Manager_InvokesDismissedCallbackOnce();
            tests.RootPresenter_RoutesRequestsToMatchingAnchorContainers();
            tests.Installer_SourceBindsClickableToastItemPrefab();
        }

        [Test]
        public void ToastSettings_DefaultsAreUsableForAnyGame()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();

            Assert.AreEqual(3, settings.MaxVisible);
            Assert.AreEqual(8, settings.MaxQueued);
            Assert.AreEqual(ToastOverflowPolicy.DropOldest, settings.OverflowPolicy);
            Assert.AreEqual(ToastDuplicatePolicy.RefreshExisting, settings.DuplicatePolicy);
            Assert.Greater(settings.Spacing, 0f);
            Assert.Greater(settings.GetDuration(ToastSeverity.Info), 0f);
            Assert.Greater(settings.GetDuration(ToastSeverity.Critical), settings.GetDuration(ToastSeverity.Info));
        }

        [Test]
        public void Resolver_UsesTextKeyBeforeFallbackMessage()
        {
            var request = new ToastRequest
            {
                Message = "Fallback message",
                TextKey = "PurchaseDeny"
            };
            var resolver = new TestResolver();

            Assert.AreEqual("Cannot buy this", ToastSettings.ResolveText(request, resolver));
            Assert.AreEqual("Fallback message", ToastSettings.ResolveText(request, null));

            var keyOnly = new ToastRequest { TextKey = "message_key" };
            Assert.AreEqual("message_key", ToastSettings.ResolveText(keyOnly, null));
        }

        [Test]
        public void Manager_RefreshesDuplicateWhenPolicySaysRefresh()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            var first = manager.Show(new ToastRequest { Message = "A", DedupeKey = "same" });
            var second = manager.Show(new ToastRequest
            {
                Message = "B",
                DedupeKey = "same",
                GroupKey = "new-group",
                Severity = ToastSeverity.Warning,
                Priority = ToastPriority.High,
                Anchor = ToastAnchor.BottomCenter,
                Duration = 5f,
                DismissOnClick = false
            });

            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual("B", second.Request.Message);
            Assert.AreEqual("new-group", second.Request.GroupKey);
            Assert.AreEqual(ToastSeverity.Warning, second.Request.Severity);
            Assert.AreEqual(ToastPriority.High, second.Request.Priority);
            Assert.AreEqual(ToastAnchor.BottomCenter, second.Request.Anchor);
            Assert.AreEqual(5f, second.Request.Duration);
            Assert.IsFalse(second.Request.DismissOnClick);
        }

        [Test]
        public void Manager_DropsOldestWhenVisibleLimitIsReached()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            var first = manager.Show(ToastRequest.Text("one"));
            manager.Show(ToastRequest.Text("two"));
            manager.Show(ToastRequest.Text("three"));
            manager.Show(ToastRequest.Text("four"));

            Assert.IsTrue(first.IsDismissed);
            Assert.AreEqual(3, manager.ActiveCount);
        }

        [Test]
        public void Manager_QueuePolicyDropsIncomingWhenQueueLimitIsZero()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 1, 0);
            manager.Configure(settings, null, null);

            manager.Show(ToastRequest.Text("one"));
            var dropped = manager.Show(ToastRequest.Text("two"));

            Assert.IsNull(dropped);
            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(0, manager.QueuedCount);
        }

        [Test]
        public void Manager_ClearGroupRemovesQueuedRequests()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            SetSettingsPolicy(settings, ToastOverflowPolicy.Queue, 1, 4);
            manager.Configure(settings, null, null);

            manager.Show(new ToastRequest { Message = "active", GroupKey = "interaction" });
            manager.Show(new ToastRequest { Message = "queued interaction", GroupKey = "interaction" });
            manager.Show(new ToastRequest { Message = "queued other", GroupKey = "other" });

            manager.ClearGroup("interaction");

            Assert.AreEqual(1, manager.ActiveCount);
            Assert.AreEqual(0, manager.QueuedCount);
        }

        [Test]
        public void Manager_RepositionsToastsPerAnchor()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var topOne = manager.Show(new ToastRequest { Message = "top 1", Anchor = ToastAnchor.TopRight });
            var bottomOne = manager.Show(new ToastRequest { Message = "bottom 1", Anchor = ToastAnchor.BottomCenter });
            var topTwo = manager.Show(new ToastRequest { Message = "top 2", Anchor = ToastAnchor.TopRight });

            Assert.AreEqual(0, presenter.GetIndex(topOne));
            Assert.AreEqual(0, presenter.GetIndex(bottomOne));
            Assert.AreEqual(1, presenter.GetIndex(topTwo));
        }

        [Test]
        public void Manager_BottomAnchorsStackUpward()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var first = manager.Show(new ToastRequest { Message = "bottom 1", Anchor = ToastAnchor.BottomCenter });
            var second = manager.Show(new ToastRequest { Message = "bottom 2", Anchor = ToastAnchor.BottomCenter });

            Assert.AreEqual(0, presenter.GetStackDirection(first));
            Assert.AreEqual(-1, presenter.GetStackDirection(second));
        }

        [Test]
        public void Manager_UsesOverrideColorForAccentOnly()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            var presenter = new RecordingPresenter();
            manager.Configure(settings, null, presenter);

            var request = new ToastRequest
            {
                Message = "colored",
                Severity = ToastSeverity.Warning,
                OverrideColor = Color.magenta
            };
            var handle = manager.Show(request);
            var normalStyle = settings.GetStyle(ToastSeverity.Warning);
            var shownStyle = presenter.GetStyle(handle);

            Assert.AreEqual(Color.magenta, shownStyle.AccentColor);
            Assert.AreEqual(normalStyle.BackgroundColor, shownStyle.BackgroundColor);
            Assert.AreEqual(normalStyle.TextColor, shownStyle.TextColor);
        }

        [Test]
        public void Manager_ClearGroupOnlyClearsMatchingGroup()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            var combat = manager.Show(new ToastRequest { Message = "hit", GroupKey = "combat" });
            var ui = manager.Show(new ToastRequest { Message = "copy", GroupKey = "ui" });

            manager.ClearGroup("combat");

            Assert.IsTrue(combat.IsDismissed);
            Assert.IsFalse(ui.IsDismissed);
        }

        [Test]
        public void Manager_InvokesDismissedCallbackOnce()
        {
            var manager = new ToastManager();
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            manager.Configure(settings, null, null);

            int callbackCount = 0;
            var handle = manager.Show(new ToastRequest
            {
                Message = "close me",
                OnDismissed = _ => callbackCount++
            });

            handle.Dismiss();
            handle.Dismiss();

            Assert.AreEqual(1, callbackCount);
        }

        [Test]
        public void RootPresenter_DisableClearsRuntimeState()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();

            var itemPrefabObject = new GameObject("ToastItemPrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastItemView));
            var itemPrefab = itemPrefabObject.GetComponent<ToastItemView>();

            var rootObject = new GameObject("ToastRoot", typeof(RectTransform), typeof(ToastRootPresenter));
            var topContainer = CreateContainer(rootObject.transform, ToastAnchor.TopRight, itemPrefab);

            var presenter = rootObject.GetComponent<ToastRootPresenter>();
            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("settings").objectReferenceValue = settings;
            var containers = presenterObject.FindProperty("containers");
            containers.arraySize = 1;
            containers.GetArrayElementAtIndex(0).objectReferenceValue = topContainer;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();
            presenter.RebuildLookup();
            ToastApi.Configure(settings, null, presenter);

            ToastApi.Show("visible");
            typeof(ToastRootPresenter)
                .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(presenter, null);

            Assert.AreEqual(0, ToastApi.Runtime.ActiveCount);

            Object.DestroyImmediate(rootObject);
            Object.DestroyImmediate(itemPrefabObject);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void RootPresenter_RoutesRequestsToMatchingAnchorContainers()
        {
            var settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();

            var itemPrefabObject = new GameObject("ToastItemPrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastItemView));
            var itemPrefab = itemPrefabObject.GetComponent<ToastItemView>();

            var rootObject = new GameObject("ToastRoot", typeof(RectTransform), typeof(ToastRootPresenter));
            var topContainer = CreateContainer(rootObject.transform, ToastAnchor.TopRight, itemPrefab);
            var bottomContainer = CreateContainer(rootObject.transform, ToastAnchor.BottomCenter, itemPrefab);

            var presenter = rootObject.GetComponent<ToastRootPresenter>();
            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("settings").objectReferenceValue = settings;
            var containers = presenterObject.FindProperty("containers");
            containers.arraySize = 2;
            containers.GetArrayElementAtIndex(0).objectReferenceValue = topContainer;
            containers.GetArrayElementAtIndex(1).objectReferenceValue = bottomContainer;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();
            presenter.RebuildLookup();

            var manager = new ToastManager();
            manager.Configure(settings, null, presenter);
            manager.Show(new ToastRequest { Message = "top", Anchor = ToastAnchor.TopRight });
            manager.Show(new ToastRequest { Message = "bottom", Anchor = ToastAnchor.BottomCenter });

            Assert.AreEqual(1, topContainer.VisibleCount);
            Assert.AreEqual(1, bottomContainer.VisibleCount);

            Object.DestroyImmediate(rootObject);
            Object.DestroyImmediate(itemPrefabObject);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Installer_SourceBindsClickableToastItemPrefab()
        {
            var source = System.IO.File.ReadAllText("Packages/com.zerogamestudio.zeroengine.ui/Editor/Toast/ToastInstaller.cs");

            StringAssert.Contains("typeof(Button)", source);
            StringAssert.Contains("serialized.FindProperty(\"button\").objectReferenceValue = button", source);
        }

        private sealed class TestResolver : IToastTextResolver
        {
            public string ResolveToastText(ToastRequest request)
            {
                return request.TextKey == "PurchaseDeny" ? "Cannot buy this" : request.Message;
            }
        }

        private sealed class RecordingPresenter : IToastPresenter
        {
            private readonly System.Collections.Generic.Dictionary<int, ToastStyle> styles = new System.Collections.Generic.Dictionary<int, ToastStyle>();
            private readonly System.Collections.Generic.Dictionary<int, int> indices = new System.Collections.Generic.Dictionary<int, int>();
            private readonly System.Collections.Generic.Dictionary<int, int> directions = new System.Collections.Generic.Dictionary<int, int>();

            public void ShowToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
            {
                styles[handle.Id] = style;
            }

            public void RefreshToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings)
            {
                styles[handle.Id] = style;
            }

            public void DismissToast(ToastHandle handle, ToastDismissReason reason) { }

            public void RepositionToast(ToastHandle handle, int index, float spacing)
            {
                indices[handle.Id] = Mathf.Abs(index);
                directions[handle.Id] = index;
            }

            public void ClearAll() { }

            public ToastStyle GetStyle(ToastHandle handle)
            {
                return styles[handle.Id];
            }

            public int GetIndex(ToastHandle handle)
            {
                return indices[handle.Id];
            }

            public int GetStackDirection(ToastHandle handle)
            {
                return directions[handle.Id];
            }
        }

        private static void SetSettingsPolicy(ToastSettings settings, ToastOverflowPolicy overflowPolicy, int maxVisible, int maxQueued)
        {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("overflowPolicy").enumValueIndex = (int)overflowPolicy;
            serialized.FindProperty("maxVisible").intValue = maxVisible;
            serialized.FindProperty("maxQueued").intValue = maxQueued;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ToastContainer CreateContainer(Transform parent, ToastAnchor anchor, ToastItemView itemPrefab)
        {
            var containerObject = new GameObject(anchor.ToString(), typeof(RectTransform), typeof(ToastContainer));
            containerObject.transform.SetParent(parent, false);
            var container = containerObject.GetComponent<ToastContainer>();
            var serialized = new SerializedObject(container);
            serialized.FindProperty("anchor").enumValueIndex = (int)anchor;
            serialized.FindProperty("itemRoot").objectReferenceValue = containerObject.GetComponent<RectTransform>();
            serialized.FindProperty("itemPrefab").objectReferenceValue = itemPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return container;
        }
    }
}
